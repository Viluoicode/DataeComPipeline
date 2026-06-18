using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp;

/// Design-time factory used only by `dotnet ef` (migrations add / script).
/// Without it the EF tools would execute Program.cs (which runs DatabaseInitializer
/// against a live SQL Server). The connection string here is a placeholder —
/// `migrations add` never opens a connection; runtime migrations are applied by
/// <see cref="ECommerPipeline.Infrastructure.Initialization.DatabaseInitializer"/>.
public class OltpDbContextFactory : IDesignTimeDbContextFactory<OltpDbContext>
{
    public OltpDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OltpDbContext>()
            .UseSqlServer("Server=localhost;Database=ECommerPipeline_DesignTime;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new OltpDbContext(options);
    }
}
