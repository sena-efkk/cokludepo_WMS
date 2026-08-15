using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wms.Integration.Messaging;
using Wms.Integration.Telemetry;

namespace Wms.Integration.Outbox;

public sealed record OutboxDispatchResult(int Dispatched, int Failed, int Pending);

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IRabbitMqPublisher publisher,
    ILogger<OutboxDispatcher> logger)
{
    private const int BatchSize = 50;

    public async Task<OutboxDispatchResult> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var dispatched = 0;
        var failed = 0;

        using var scope = scopeFactory.CreateScope();
        var stores = scope.ServiceProvider.GetServices<IOutboxStore>().ToList();

        foreach (var store in stores)
        {
            var pending = await store.FetchPendingAsync(BatchSize, cancellationToken);
            foreach (var message in pending)
            {
                try
                {
                    await publisher.PublishAsync(message, cancellationToken);
                    await store.MarkPublishedAsync(message.Id, DateTime.UtcNow, cancellationToken);
                    dispatched++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var backoff = message.AttemptCount switch
                    {
                        0 => TimeSpan.FromSeconds(5),
                        1 => TimeSpan.FromSeconds(30),
                        _ => TimeSpan.FromMinutes(5),
                    };
                    await store.MarkFailedAsync(message.Id, exception.Message, DateTime.UtcNow + backoff, cancellationToken);
                    failed++;
                    WmsMetrics.OutboxPublishFailuresTotal.Add(1);
                    logger.LogWarning(
                        "Outbox publish failed ({EventType} {EventId}): {Error} — retry at {NextAttempt}",
                        message.EventType,
                        message.EventId,
                        exception.Message,
                        DateTime.UtcNow + backoff);
                }
            }
        }

        await UpdatePendingGaugeAsync(stores, cancellationToken);

        return new OutboxDispatchResult(dispatched, failed, 0);
    }

    private static async Task UpdatePendingGaugeAsync(
        IReadOnlyList<IOutboxStore> stores,
        CancellationToken cancellationToken)
    {
        var totalPending = 0;
        DateTime? oldest = null;
        foreach (var store in stores)
        {
            totalPending += await store.CountPendingAsync(cancellationToken);
            var storeOldest = await store.GetOldestPendingCreatedAtAsync(cancellationToken);
            if (storeOldest is not null && (oldest is null || storeOldest < oldest))
            {
                oldest = storeOldest;
            }
        }

        var oldestSeconds = oldest is null ? 0d : Math.Max(0d, (DateTime.UtcNow - oldest.Value).TotalSeconds);
        WmsMetrics.SetOutboxPending(totalPending, oldestSeconds);
    }
}

public sealed class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchOnceAsync(stoppingToken);
                if (result.Dispatched > 0 || result.Failed > 0)
                {
                    logger.LogInformation("Outbox dispatch: {Dispatched} published, {Failed} failed", result.Dispatched, result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
