using ClosedXML.Excel;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Import.DTOs;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Import;

/// <summary>
/// Excel import service using ClosedXML. Each sheet's first row is treated as header.
/// All imports are transactional per-file: on any unrecoverable error, nothing is saved.
/// Per-row validation errors are collected and returned without aborting the rest.
/// </summary>
public class ExcelImportService : IImportService
{
    private readonly OltpDbContext _db;
    private readonly ILogger<ExcelImportService> _logger;

    public ExcelImportService(OltpDbContext db, ILogger<ExcelImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ====================================================================
    //  CUSTOMERS
    // ====================================================================
    public async Task<ImportResult> ImportCustomersAsync(Stream xlsx, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsx);
        var sheet = wb.Worksheets.First();
        var errors = new List<ImportError>();
        var toAdd = new List<Customer>();

        var existingEmails = await _db.Customers.AsNoTracking()
            .Select(c => c.Email)
            .ToListAsync(ct);
        var emailSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);

        var rows = sheet.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new List<IXLRangeRow>();
        var rowIndex = 1;

        foreach (var row in rows)
        {
            rowIndex++;
            try
            {
                var fullName = row.Cell(1).GetString().Trim();
                var email    = row.Cell(2).GetString().Trim();
                var phone    = row.Cell(3).GetString().Trim();
                var city     = row.Cell(4).GetString().Trim();

                if (string.IsNullOrWhiteSpace(fullName)) { errors.Add(new(rowIndex, "FullName is required")); continue; }
                if (string.IsNullOrWhiteSpace(email))    { errors.Add(new(rowIndex, "Email is required"));    continue; }
                if (!emailSet.Add(email))                 { errors.Add(new(rowIndex, $"Duplicate email: {email}")); continue; }

                toAdd.Add(new Customer
                {
                    FullName = fullName,
                    Email    = email,
                    Phone    = string.IsNullOrEmpty(phone) ? null : phone,
                    City     = string.IsNullOrEmpty(city)  ? null : city,
                });
            }
            catch (Exception ex)
            {
                errors.Add(new(rowIndex, ex.Message));
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Customers.AddRange(toAdd);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} customers ({Errors} errors)", toAdd.Count, errors.Count);
        return new ImportResult(rows.Count, toAdd.Count, errors.Count, errors);
    }

    // ====================================================================
    //  PRODUCTS
    // ====================================================================
    public async Task<ImportResult> ImportProductsAsync(Stream xlsx, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsx);
        var sheet = wb.Worksheets.First();
        var errors = new List<ImportError>();
        var toAdd = new List<Product>();

        var existingSkus = await _db.Products.AsNoTracking()
            .Select(p => p.Sku)
            .ToListAsync(ct);
        var skuSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        var rows = sheet.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new List<IXLRangeRow>();
        var rowIndex = 1;

        foreach (var row in rows)
        {
            rowIndex++;
            try
            {
                var sku      = row.Cell(1).GetString().Trim();
                var name     = row.Cell(2).GetString().Trim();
                var category = row.Cell(3).GetString().Trim();
                var brand    = row.Cell(4).GetString().Trim();
                var price    = row.Cell(5).GetValue<decimal>();
                var stock    = row.Cell(6).GetValue<int>();

                if (string.IsNullOrWhiteSpace(sku))      { errors.Add(new(rowIndex, "Sku is required"));      continue; }
                if (string.IsNullOrWhiteSpace(name))     { errors.Add(new(rowIndex, "Name is required"));     continue; }
                if (string.IsNullOrWhiteSpace(category)) { errors.Add(new(rowIndex, "Category is required")); continue; }
                if (price <= 0)                          { errors.Add(new(rowIndex, "Price must be > 0"));   continue; }
                if (!skuSet.Add(sku))                    { errors.Add(new(rowIndex, $"Duplicate Sku: {sku}")); continue; }

                toAdd.Add(new Product
                {
                    Sku = sku,
                    Name = name,
                    Category = category,
                    Brand = string.IsNullOrEmpty(brand) ? null : brand,
                    Price = price,
                    StockQuantity = stock,
                });
            }
            catch (Exception ex)
            {
                errors.Add(new(rowIndex, ex.Message));
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Products.AddRange(toAdd);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} products ({Errors} errors)", toAdd.Count, errors.Count);
        return new ImportResult(rows.Count, toAdd.Count, errors.Count, errors);
    }

    // ====================================================================
    //  ORDERS — each row = 1 order line; orders are grouped by OrderRef column
    // ====================================================================
    public async Task<ImportResult> ImportOrdersAsync(Stream xlsx, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsx);
        var sheet = wb.Worksheets.First();
        var errors = new List<ImportError>();

        var customers = await _db.Customers.AsNoTracking()
            .ToDictionaryAsync(c => c.Email, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);
        var products = await _db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Sku, p => p, StringComparer.OrdinalIgnoreCase, ct);

        var rows = sheet.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new List<IXLRangeRow>();
        var rowIndex = 1;

        // Group line-rows by OrderRef
        var groups = new Dictionary<string, List<(int RowIdx, string CustomerEmail, string Sku, int Qty)>>();

        foreach (var row in rows)
        {
            rowIndex++;
            try
            {
                var orderRef     = row.Cell(1).GetString().Trim();
                var customerEmail = row.Cell(2).GetString().Trim();
                var sku          = row.Cell(3).GetString().Trim();
                var qty          = row.Cell(4).GetValue<int>();

                if (string.IsNullOrWhiteSpace(orderRef))     { errors.Add(new(rowIndex, "OrderRef is required"));     continue; }
                if (string.IsNullOrWhiteSpace(customerEmail)) { errors.Add(new(rowIndex, "CustomerEmail is required")); continue; }
                if (string.IsNullOrWhiteSpace(sku))           { errors.Add(new(rowIndex, "Sku is required"));           continue; }
                if (qty <= 0)                                 { errors.Add(new(rowIndex, "Quantity must be > 0"));      continue; }

                if (!groups.TryGetValue(orderRef, out var list))
                {
                    list = new();
                    groups[orderRef] = list;
                }
                list.Add((rowIndex, customerEmail, sku, qty));
            }
            catch (Exception ex)
            {
                errors.Add(new(rowIndex, ex.Message));
            }
        }

        var orders = new List<Order>();
        foreach (var (orderRef, lines) in groups)
        {
            var firstRow = lines[0].RowIdx;
            if (!customers.TryGetValue(lines[0].CustomerEmail, out var customerId))
            {
                errors.Add(new(firstRow, $"Customer not found by email: {lines[0].CustomerEmail}"));
                continue;
            }

            var items = new List<OrderItem>();
            var hasError = false;
            foreach (var (rIdx, _, sku, qty) in lines)
            {
                if (!products.TryGetValue(sku, out var p))
                {
                    errors.Add(new(rIdx, $"Product not found by SKU: {sku}"));
                    hasError = true;
                    continue;
                }
                items.Add(new OrderItem
                {
                    ProductId = p.Id,
                    Quantity = qty,
                    UnitPrice = p.Price,
                    LineTotal = p.Price * qty,
                });
            }

            if (hasError || items.Count == 0) continue;

            orders.Add(new Order
            {
                OrderNumber = $"ORD-XLS-{DateTime.UtcNow:yyyyMMddHHmmss}-{orderRef}",
                CustomerId  = customerId,
                OrderDate   = DateTime.UtcNow,
                Status      = OrderStatus.Pending,
                Items       = items,
                TotalAmount = items.Sum(i => i.LineTotal),
            });
        }

        if (orders.Count > 0)
        {
            _db.Orders.AddRange(orders);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} orders ({Errors} errors)", orders.Count, errors.Count);
        return new ImportResult(rows.Count, orders.Count, errors.Count, errors);
    }

    // ====================================================================
    //  TEMPLATES
    // ====================================================================
    public Task<byte[]> GetTemplateAsync(ImportTemplate kind, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add(kind.ToString());

        switch (kind)
        {
            case ImportTemplate.Customers:
                sheet.Cell(1, 1).Value = "FullName";
                sheet.Cell(1, 2).Value = "Email";
                sheet.Cell(1, 3).Value = "Phone";
                sheet.Cell(1, 4).Value = "City";
                sheet.Cell(2, 1).Value = "Nguyen Van A";
                sheet.Cell(2, 2).Value = "a@example.com";
                sheet.Cell(2, 3).Value = "0901234567";
                sheet.Cell(2, 4).Value = "HCM";
                break;

            case ImportTemplate.Products:
                sheet.Cell(1, 1).Value = "Sku";
                sheet.Cell(1, 2).Value = "Name";
                sheet.Cell(1, 3).Value = "Category";
                sheet.Cell(1, 4).Value = "Brand";
                sheet.Cell(1, 5).Value = "Price";
                sheet.Cell(1, 6).Value = "StockQuantity";
                sheet.Cell(2, 1).Value = "SKU-EXAMPLE";
                sheet.Cell(2, 2).Value = "Example Product";
                sheet.Cell(2, 3).Value = "Electronics";
                sheet.Cell(2, 4).Value = "BrandX";
                sheet.Cell(2, 5).Value = 1_000_000;
                sheet.Cell(2, 6).Value = 100;
                break;

            case ImportTemplate.Orders:
                sheet.Cell(1, 1).Value = "OrderRef";
                sheet.Cell(1, 2).Value = "CustomerEmail";
                sheet.Cell(1, 3).Value = "Sku";
                sheet.Cell(1, 4).Value = "Quantity";
                sheet.Cell(2, 1).Value = "ORDER-1";
                sheet.Cell(2, 2).Value = "a@example.com";
                sheet.Cell(2, 3).Value = "SKU-EXAMPLE";
                sheet.Cell(2, 4).Value = 2;
                sheet.Cell(3, 1).Value = "ORDER-1"; // same order, second line
                sheet.Cell(3, 2).Value = "a@example.com";
                sheet.Cell(3, 3).Value = "SKU-OTHER";
                sheet.Cell(3, 4).Value = 1;
                break;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }
}
