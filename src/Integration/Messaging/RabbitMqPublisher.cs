using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using Wms.Integration.Outbox;

namespace Wms.Integration.Messaging;

public interface IRabbitMqPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);

    Task DeclareQueueAsync(string queueName, string? routingKey, CancellationToken cancellationToken);

    Task<RabbitMqStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed record RabbitMqStatus(bool IsHealthy, string Detail);

public sealed class RabbitMqPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var channel = await EnsureChannelAsync(cancellationToken);

        var envelope = IntegrationEventEnvelope.From(message);
        var body = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(envelope));

        var routingKey = $"{message.EventType}.v{message.EventVersion}";

        var properties = new BasicProperties
        {
            MessageId = message.EventId.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
            ContentType = "application/json",
            Type = message.EventType,
            CorrelationId = message.CorrelationId?.ToString(),
            Persistent = true,
        };

        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogDebug("Outbox event published: {EventType} {EventId}", message.EventType, message.EventId);
    }

    public async Task DeclareQueueAsync(string queueName, string? routingKey, CancellationToken cancellationToken)
    {
        var channel = await EnsureChannelAsync(cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _options.Exchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queueName,
            },
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: $"{queueName}-dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: $"{queueName}-dlq",
            exchange: _options.DeadLetterExchange,
            routingKey: queueName,
            arguments: null,
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(routingKey))
        {
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: _options.Exchange,
                routingKey: routingKey,
                arguments: null,
                cancellationToken: cancellationToken);
        }
    }

    public async Task<RabbitMqStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            return new RabbitMqStatus(true, $"connected to {_options.Host}:{_options.Port} (channel {channel.ChannelNumber})");
        }
        catch (Exception exception)
        {
            return new RabbitMqStatus(false, exception.Message);
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _channel.IsOpen)
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null && _channel.IsOpen)
            {
                return _channel;
            }

            _connection?.Dispose();
            _channel?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                RequestedConnectionTimeout = _options.ConnectionTimeout,
                AutomaticRecoveryEnabled = true,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _gate.Dispose();
    }
}
