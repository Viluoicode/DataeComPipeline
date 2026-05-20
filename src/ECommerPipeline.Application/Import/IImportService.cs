using ECommerPipeline.Application.Import.DTOs;

namespace ECommerPipeline.Application.Import;

public interface IImportService
{
    Task<ImportResult> ImportCustomersAsync(Stream xlsx, CancellationToken ct = default);
    Task<ImportResult> ImportProductsAsync(Stream xlsx, CancellationToken ct = default);
    Task<ImportResult> ImportOrdersAsync(Stream xlsx, CancellationToken ct = default);

    /// Returns an .xlsx template stream with headers + 1 example row.
    Task<byte[]> GetTemplateAsync(ImportTemplate kind, CancellationToken ct = default);
}

public enum ImportTemplate { Customers, Products, Orders }
