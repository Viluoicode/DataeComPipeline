namespace ECommerPipeline.Application.Products.DTOs;

public record ProductLookupDto(
    long Id,
    string Sku,
    string Name,
    string Category,
    string? Brand,
    decimal Price,
    int StockQuantity,
    string? ImageUrl);   // serve path (/api/products/{id}/image) when an image is uploaded, else null

// ---- Phase 8: admin catalog management ----
public record CreateProductRequest(
    string Sku,
    string Name,
    string Category,
    string? Brand,
    decimal Price,
    int StockQuantity);

/// Sku is immutable (it's the business key) — only mutable attributes here.
public record UpdateProductRequest(
    string Name,
    string Category,
    string? Brand,
    decimal Price,
    int StockQuantity);
