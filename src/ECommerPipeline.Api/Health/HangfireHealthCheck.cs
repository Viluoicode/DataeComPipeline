using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerPipeline.Api.Health;

/// Reports whether at least one Hangfire server is alive to run the recurring
/// jobs (ETL, data quality, outbox, backup). Returns Degraded (not Unhealthy)
/// when none are seen, so /health stays 200 for the container probe — the job
/// outage is still visible in the health payload.
public class HangfireHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var servers = JobStorage.Current.GetMonitoringApi().Servers();
            return Task.FromResult(servers.Count > 0
                ? HealthCheckResult.Healthy($"{servers.Count} Hangfire server(s) alive")
                : HealthCheckResult.Degraded("No Hangfire servers — background jobs not running"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Hangfire storage unreachable", ex));
        }
    }
}
