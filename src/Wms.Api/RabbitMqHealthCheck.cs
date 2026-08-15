using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Integration.Messaging;

namespace Wms.Api;

public sealed class RabbitMqHealthCheck(IRabbitMqPublisher publisher) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = await publisher.GetStatusAsync(cancellationToken);
        return status.IsHealthy
            ? HealthCheckResult.Healthy(status.Detail)
            : HealthCheckResult.Unhealthy(status.Detail);
    }
}
