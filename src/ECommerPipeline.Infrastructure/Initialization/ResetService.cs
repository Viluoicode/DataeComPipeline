using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ECommerPipeline.Infrastructure.Initialization;

/// Wipe Orders + OLAP fact + watermark — so you can run the full create→ETL→report flow repeatedly.
/// Customers and Products are kept (seed survives).
public class ResetService
{
    private readonly OltpDbContext _oltp;
    private readonly IConfiguration _config;
    private readonly IOlapConnectionFactory _olap;

    public ResetService(OltpDbContext oltp, IConfiguration config, IOlapConnectionFactory olap)
    {
        _oltp = oltp;
        _config = config;
        _olap = olap;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _oltp.Database.ExecuteSqlRawAsync("DELETE FROM OrderItems; DELETE FROM Orders;", ct);

        using var conn = (SqlConnection)_olap.CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(@"
            TRUNCATE TABLE fact.SalesOrderItem;
            DELETE FROM etl.Watermark;", cancellationToken: ct));
    }
}
