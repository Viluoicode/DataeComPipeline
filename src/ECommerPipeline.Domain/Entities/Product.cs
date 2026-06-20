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

    /// Stored image file name (e.g. "42.png") under the product-images storage dir;
    /// null = use the generated placeholder. Served via GET /api/products/{id}/image.
    public string? ImageUrl { get; set; }

    // Optimistic-concurrency token (SQL Server rowversion) — guards against lost
    // updates on stock when two orders decrement the same product concurrently.
    // Nullable in CLR so EF InMemory (tests) doesn't require a value; SQL Server
    // auto-generates it.
    public byte[]? RowVersion { get; set; }
}
