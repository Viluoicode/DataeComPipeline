using Microsoft.Extensions.Configuration;

namespace ECommerPipeline.Infrastructure.Products;

/// File storage for product images. Files live under Storage:ProductImages
/// (mount a Docker volume there to persist). Served back through the API
/// (GET /api/products/{id}/image) so it works behind the nginx /api proxy
/// without extra web-server config.
public class ProductImageStorage
{
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private readonly string _dir;

    public ProductImageStorage(IConfiguration config)
    {
        _dir = config["Storage:ProductImages"]
               ?? Path.Combine(AppContext.BaseDirectory, "uploads", "products");
        Directory.CreateDirectory(_dir);
    }

    public bool IsAllowedExtension(string ext) => AllowedTypes.ContainsKey(ext);

    public async Task<string> SaveAsync(long productId, Stream content, string ext, CancellationToken ct)
    {
        // One image per product — clear any previous variant first.
        foreach (var f in Directory.GetFiles(_dir, $"{productId}.*"))
            File.Delete(f);

        var fileName = $"{productId}{ext.ToLowerInvariant()}";
        await using var fs = File.Create(Path.Combine(_dir, fileName));
        await content.CopyToAsync(fs, ct);
        return fileName;
    }

    public (Stream Stream, string ContentType)? Open(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        if (!File.Exists(path)) return null;

        var ext = Path.GetExtension(fileName);
        var contentType = AllowedTypes.TryGetValue(ext, out var t) ? t : "application/octet-stream";
        return (File.OpenRead(path), contentType);
    }
}
