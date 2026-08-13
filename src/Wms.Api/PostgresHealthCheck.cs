using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wms.Api;

internal sealed class PostgresHealthCheck(Npgsql.NpgsqlDataSource? dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (dataSource is null)
        {
            return HealthCheckResult.Degraded("ConnectionStrings:WmsDatabase yapılandırılmamış.");
        }

        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL bağlantısı ve sorgu çalışıyor.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL erişilemiyor.", exception);
        }
    }
}
