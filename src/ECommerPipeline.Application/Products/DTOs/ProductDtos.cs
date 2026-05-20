namespace ECommerPipeline.Application.Products.DTOs;

public record ProductLookupDto(
    long Id,
    string Sku,
    string Name,
    string Category,
    string? Brand,
    decimal Price,
    int StockQuantity);
