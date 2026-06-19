namespace ECommerPipeline.Application.Reports.DTOs;

public record SalesByCategoryRow(string Category, long OrderCount, decimal TotalRevenue);

public record SalesByDayRow(DateTime Day, long OrderCount, decimal TotalRevenue);

public record TopProductRow(long ProductId, string Sku, string Name, long TotalQuantity, decimal TotalRevenue);

// ---- Phase 4: business-state analytics (payment / funnel / inventory) ----
public record PaymentMethodSalesRow(
    int PaymentMethod, string MethodName, long OrderCount, long PaidOrderCount,
    decimal TotalRevenue, decimal PaidRevenue);

public record OrderFunnelRow(string Stage, int StageOrder, long OrderCount);

public record ProductInventoryRow(
    long ProductId, string Sku, string ProductName, string Category,
    int CurrentStock, long UnitsSold, bool LowStock);
