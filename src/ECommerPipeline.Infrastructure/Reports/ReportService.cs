using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Application.Reports.DTOs;

namespace ECommerPipeline.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly IOlapConnectionFactory _factory;

    public ReportService(IOlapConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(DateTime from, DateTime to, CancellationToken ct = default)
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

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SalesByCategoryRow>(
            new CommandDefinition(sql, new { From = from.Date, To = to.Date }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SalesByDayRow>> GetSalesByDayAsync(DateTime from, DateTime to, CancellationToken ct = default)
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

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SalesByDayRow>(
            new CommandDefinition(sql, new { From = from.Date, To = to.Date }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateTime from, DateTime to, int top = 10, CancellationToken ct = default)
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

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TopProductRow>(
            new CommandDefinition(sql, new { From = from.Date, To = to.Date, Top = top }, cancellationToken: ct));
        return rows.AsList();
    }
}
