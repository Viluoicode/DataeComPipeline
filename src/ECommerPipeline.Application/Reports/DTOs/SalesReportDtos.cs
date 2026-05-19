namespace ECommerPipeline.Application.Reports.DTOs;

public record SalesByCategoryRow(string Category, long OrderCount, decimal TotalRevenue);

public record SalesByDayRow(DateTime Day, long OrderCount, decimal TotalRevenue);

public record TopProductRow(long ProductId, string Sku, string Name, long TotalQuantity, decimal TotalRevenue);
