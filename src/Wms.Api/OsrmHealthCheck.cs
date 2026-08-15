using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wms.Api;

/// <summary>
/// OSRM optional bir bağımlılıktır: kapalıyken Haversine fallback devreye girer.
/// Bu yüzden OSRM durumu DEGRADED olarak raporlanır, WMS DOWN olmaz.
/// </summary>
public sealed class OsrmHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("OsrmRouteProvider");
            var response = await client.GetAsync("/nearest/v1/driving/29.0,40.0", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("OSRM erişilebilir.")
                : HealthCheckResult.Degraded($"OSRM hata döndü: {(int)response.StatusCode}");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded($"OSRM erişilemiyor ({exception.GetType().Name}) — Haversine fallback aktif.");
        }
    }
}
