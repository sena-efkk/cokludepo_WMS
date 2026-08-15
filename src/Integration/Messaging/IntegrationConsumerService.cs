using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Wms.Integration.Contracts;
using Wms.Integration.Telemetry;

namespace Wms.Integration.Messaging;

public enum ConsumerProcessingResult
{
    Ack = 1,
    NackRequeue = 2,
    NackToDlq = 3,
}

public interface IIntegrationConsumer
{
    string Name { get; }

    string QueueName { get; }

    IReadOnlyList<string> BindingRoutingKeys { get; }

    Task<ConsumerProcessingResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class IntegrationConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqPublisher _publisher;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<IntegrationConsumerService> _logger;

    public IntegrationConsumerService(
        IServiceScopeFactory scopeFactory,
        RabbitMqPublisher publisher,
        IOptions<RabbitMqOptions> options,
        ILogger<IntegrationConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<ConsumerDescriptor> descriptors;
        using (var scope = _scopeFactory.CreateScope())
        {
            descriptors = scope.ServiceProvider
                .GetServices<IIntegrationConsumer>()
                .Select(c => new ConsumerDescriptor(c.Name, c.QueueName, c.BindingRoutingKeys))
                .ToList();
        }

        foreach (var descriptor in descriptors)
        {
            await Task.Run(() => RunConsumerAsync(descriptor, stoppingToken), stoppingToken);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task RunConsumerAsync(ConsumerDescriptor descriptor, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeQueueAsync(descriptor, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Consumer {Consumer} bağlantı hatası — 5 sn sonra yeniden denenir", descriptor.Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeQueueAsync(ConsumerDescriptor descriptor, CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Value.Host,
            Port = _options.Value.Port,
            UserName = _options.Value.Username,
            Password = _options.Value.Password,
            VirtualHost = _options.Value.VirtualHost,
            RequestedConnectionTimeout = _options.Value.ConnectionTimeout,
            AutomaticRecoveryEnabled = true,
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.BasicQosAsync(0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        await _publisher.DeclareQueueAsync(descriptor.QueueName, null, stoppingToken);
        foreach (var routingKey in descriptor.BindingRoutingKeys)
        {
            await channel.QueueBindAsync(
                queue: descriptor.QueueName,
                exchange: _options.Value.Exchange,
                routingKey: routingKey,
                arguments: null,
                cancellationToken: stoppingToken);
        }

        var consumerInstance = new AsyncEventingBasicConsumer(channel);
        consumerInstance.ReceivedAsync += async (_, eventArgs) =>
        {
            var result = ConsumerProcessingResult.NackToDlq;
            try
            {
                var body = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(body)
                    ?? throw new InvalidOperationException("Boş mesaj envelope'u çözümlenemedi.");

                using var scope = _scopeFactory.CreateScope();
                var consumers = scope.ServiceProvider.GetServices<IIntegrationConsumer>();
                var consumer = consumers.FirstOrDefault(c => c.Name == descriptor.Name)
                    ?? throw new InvalidOperationException($"Consumer kayıtlı değil: {descriptor.Name}");

                result = await consumer.HandleAsync(envelope, stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Consumer {Consumer} mesaj işleme hatası (redelivered={Redelivered})",
                    descriptor.Name, eventArgs.Redelivered);
                WmsMetrics.ConsumerFailuresTotal.Add(1);
                result = eventArgs.Redelivered ? ConsumerProcessingResult.NackToDlq : ConsumerProcessingResult.NackRequeue;
            }

            if (result == ConsumerProcessingResult.NackToDlq)
            {
                WmsMetrics.DlqMessagesTotal.Add(1);
            }

            switch (result)
            {
                case ConsumerProcessingResult.Ack:
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                    break;
                case ConsumerProcessingResult.NackRequeue:
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                    break;
                default:
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    break;
            }
        };

        await channel.BasicConsumeAsync(
            queue: descriptor.QueueName,
            autoAck: false,
            consumer: consumerInstance,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private sealed record ConsumerDescriptor(string Name, string QueueName, IReadOnlyList<string> BindingRoutingKeys);
}
