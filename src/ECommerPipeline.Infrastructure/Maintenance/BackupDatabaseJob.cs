using Dapper;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Maintenance;

/// Nightly OLTP database backup (config-gated). Writes a .bak to the SQL Server's
/// filesystem at Backup:Directory (mount a Docker volume there to persist/offsite).
/// No-ops when Backup:Directory is unset, so local/dev needs zero setup.
/// Restore runbook: docs/BACKUP_RESTORE.md.
public class BackupDatabaseJob
{
    private readonly IConfiguration _config;
    private readonly ILogger<BackupDatabaseJob> _logger;

    public BackupDatabaseJob(IConfiguration config, ILogger<BackupDatabaseJob> logger)
    {
        _config = config;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var dir = _config["Backup:Directory"];
        if (string.IsNullOrWhiteSpace(dir))
        {
            _logger.LogInformation("Backup skipped — Backup:Directory not configured.");
            return;
        }

        var oltp = _config.GetConnectionString("OltpConnection")
            ?? throw new InvalidOperationException("OltpConnection missing.");
        var dbName = new SqlConnectionStringBuilder(oltp).InitialCatalog;
        var path = $"{dir.TrimEnd('/', '\\')}/{dbName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";

        // dbName is our own catalog name from config (not user input), so it's safe
        // to interpolate into the object identifier; the path goes via a T-SQL var.
        var sql = $@"
            DECLARE @path NVARCHAR(400) = @File;
            BACKUP DATABASE [{dbName}] TO DISK = @path WITH INIT, STATS = 10;";

        await using var conn = new SqlConnection(oltp);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { File = path }, commandTimeout: 600, cancellationToken: ct));

        _logger.LogInformation("OLTP database backed up to {Path}", path);
    }
}
