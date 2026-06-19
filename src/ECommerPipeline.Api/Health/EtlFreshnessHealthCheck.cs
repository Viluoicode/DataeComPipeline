using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerPipeline.Api.Health;

/// Flags a stalled ETL by checking how long ago the watermark last advanced.
/// Degraded (never Unhealthy) past the threshold so the container probe stays
/// green; "no data yet" (fresh DB before the first run) is treated as Healthy.
/// Threshold: Health:EtlMaxAgeMinutes (default 90).
public class EtlFreshnessHealthCheck : IHealthCheck
{
    private readonly IOlapConnectionFactory _olap;
    private readonly int _maxAgeMinutes;

    public EtlFreshnessHealthCheck(IOlapConnectionFactory olap, IConfiguration config)
    {
        _olap = olap;
        _maxAgeMinutes = config.GetValue("Health:EtlMaxAgeMinutes", 90);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var conn = (SqlConnection)_olap.CreateConnection();
            await conn.OpenAsync(ct);

            var lastProcessed = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT MAX(LastProcessedAt) FROM etl.Watermark;", cancellationToken: ct));

            if (lastProcessed is null)
                return HealthCheckResult.Healthy("ETL has not run yet (no watermark).");

            var ageMinutes = (DateTime.UtcNow - lastProcessed.Value).TotalMinutes;
            return ageMinutes <= _maxAgeMinutes
                ? HealthCheckResult.Healthy($"ETL last advanced {ageMinutes:F0} min ago.")
                : HealthCheckResult.Degraded(
                    $"ETL watermark is stale: {ageMinutes:F0} min (threshold {_maxAgeMinutes}).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Could not read ETL watermark.", ex);
        }
    }
}
