using ECommerPipeline.Domain.Common;

namespace ECommerPipeline.Domain.Entities;

public class Product : BaseEntity
{
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? Brand { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
