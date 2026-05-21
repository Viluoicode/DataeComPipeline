using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Application.Reports.DTOs;
using Microsoft.Data.SqlClient;

namespace ECommerPipeline.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly IOlapConnectionFactory _factory;

    public ReportService(IOlapConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT  p.Category,
                    CAST(COUNT(DISTINCT f.OrderId) AS BIGINT)    AS OrderCount,
                    CAST(SUM(f.LineTotal) AS DECIMAL(18,2))      AS TotalRevenue
            FROM    fact.SalesOrderItem f
            JOIN    dim.Product p   ON p.ProductKey = f.ProductKey
            JOIN    dim.Date    d   ON d.DateKey    = f.DateKey
            WHERE   d.[Date] BETWEEN @From AND @To
            GROUP BY p.Category
            ORDER BY TotalRevenue DESC;";

        return QueryAsync<SalesByCategoryRow>(sql, new { From = from.Date, To = to.Date }, ct);
    }

    public Task<IReadOnlyList<SalesByDayRow>> GetSalesByDayAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT  CAST(d.[Date] AS DATETIME)              AS [Day],
                    CAST(COUNT(DISTINCT f.OrderId) AS BIGINT) AS OrderCount,
                    CAST(SUM(f.LineTotal) AS DECIMAL(18,2))   AS TotalRevenue
            FROM    fact.SalesOrderItem f
            JOIN    dim.Date d ON d.DateKey = f.DateKey
            WHERE   d.[Date] BETWEEN @From AND @To
            GROUP BY d.[Date]
            ORDER BY d.[Date];";

        return QueryAsync<SalesByDayRow>(sql, new { From = from.Date, To = to.Date }, ct);
    }

    public Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateTime from, DateTime to, int top = 10, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@Top)
                   p.ProductId,
                   p.Sku,
                   p.Name,
                   CAST(SUM(f.Quantity)  AS BIGINT)        AS TotalQuantity,
                   CAST(SUM(f.LineTotal) AS DECIMAL(18,2)) AS TotalRevenue
            FROM   fact.SalesOrderItem f
            JOIN   dim.Product p ON p.ProductKey = f.ProductKey
            JOIN   dim.Date    d ON d.DateKey    = f.DateKey
            WHERE  d.[Date] BETWEEN @From AND @To
            GROUP BY p.ProductId, p.Sku, p.Name
            ORDER BY TotalRevenue DESC;";

        return QueryAsync<TopProductRow>(sql, new { From = from.Date, To = to.Date, Top = top }, ct);
    }

    /// Wraps Dapper QueryAsync so that a CancellationToken-triggered SqlException
    /// (numbers 0 / -2 / "Operation cancelled by user") is converted to a clean
    /// OperationCanceledException — letting the global exception handler return
    /// 499 (client closed request) instead of leaking a scary 500 SqlException.
    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        try
        {
            var rows = await conn.QueryAsync<T>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
            return rows.AsList();
        }
        catch (SqlException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Query cancelled by client.", ex, ct);
        }
        catch (SqlException ex) when (ex.Number is 0 or -2)
        {
            // 0 / -2 = network-level cancel from SqlClient. Treat as cancellation.
            throw new OperationCanceledException("Query cancelled.", ex, ct);
        }
    }
}
