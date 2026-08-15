using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Integration.Telemetry;

namespace Wms.Integration.Outbox;

public sealed class OutboxRetentionOptions
{
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Published outbox kayıtları için güvenli retention: YALNIZ published + RetentionDays
/// yaşındaki kayıtlar silinir. Pending/failed kayıtlara ASLA dokunulmaz.
/// </summary>
public sealed class OutboxRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxRetentionOptions> options,
    ILogger<OutboxRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.RetentionDays > 0)
                {
                    using var scope = scopeFactory.CreateScope();
                    var stores = scope.ServiceProvider.GetServices<IOutboxStore>().ToList();
                    var cutoff = DateTime.UtcNow.AddDays(-options.Value.RetentionDays);

                    foreach (var store in stores)
                    {
                        var deleted = await store.DeletePublishedOlderThanAsync(cutoff, stoppingToken);
                        if (deleted > 0)
                        {
                            logger.LogInformation("Outbox retention: {Deleted} published kayıt silindi (cutoff {Cutoff})", deleted, cutoff);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox retention cycle failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
