# Testing & CI/CD (Phase 7)

## Test suites

| Type | Where | Runs |
|---|---|---|
| **Unit** (xUnit) | `tests/ECommerPipeline.Application.Tests`, `tests/ECommerPipeline.Infrastructure.Tests` | `dotnet test` (no infra) |
| **Frontend type-check + build** | `frontend` | `npm run build` |
| **Integration (E2E)** | scaffold below — opt-in, needs Docker | CI / local with `RUN_INTEGRATION_TESTS=1` |

```bash
dotnet test            # 87 unit tests — auth/JWT, order inventory + state machine,
                       # VNPay/MoMo signature + anti-tamper, outbox dispatch, email templates
cd frontend && npm run build
```

Unit coverage focuses on the **logic that must not regress**: oversell protection,
the order state machine, payment signature verification (a forged/tampered callback
must be rejected), outbox reliability, and authorization helpers.

## CI (`.github/workflows/ci.yml`)

On push/PR to `main`:
- **backend** — restore → build (Release) → `dotnet test` with `XPlat Code Coverage`; results uploaded.
- **frontend** — `npm ci` → `tsc --noEmit` → `npm run build`.
- **docker-build** — builds API + frontend images, **Trivy** scans the API image for
  HIGH/CRITICAL CVEs (report-only — flip `exit-code: '1'` to gate), and emits an
  **SBOM** (Syft, SPDX) as an artifact.

## CD (`.github/workflows/deploy-vps.yml`)

Manual (`workflow_dispatch`) by default; uncomment the `push: main` trigger to
auto-deploy on merge. Requires repo secrets `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`,
`VPS_APP_DIR`. It SSHes in, `git reset --hard origin/main`, rebuilds via
`docker-compose.prod.yml`, then **smoke-tests `/health`** — on failure it **rolls
back** to the previous commit and redeploys.

## Integration tests (opt-in scaffold)

True end-to-end tests run the API against a real SQL Server via
[Testcontainers](https://dotnet.testcontainers.org/). They're gated so local
`dotnet test` (no Docker) stays green; enable with `RUN_INTEGRATION_TESTS=1`
(CI runners have Docker).

1. New project `tests/ECommerPipeline.IntegrationTests` referencing the Api, with
   packages `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `xunit`.

2. A gate attribute so the suite skips without Docker:

```csharp
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "1")
            Skip = "Set RUN_INTEGRATION_TESTS=1 (needs Docker) to run.";
    }
}
```

3. A fixture + `WebApplicationFactory` pointing the 3 connection strings at one
   Testcontainers SQL instance (different catalogs), with a tiny seed:

```csharp
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sql.StartAsync();
    public new async Task DisposeAsync() => await _sql.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cs = _sql.GetConnectionString();
        builder.UseSetting("ConnectionStrings:OltpConnection",     $"{cs};Database=ECP_Oltp");
        builder.UseSetting("ConnectionStrings:OlapConnection",     $"{cs};Database=ECP_Olap");
        builder.UseSetting("ConnectionStrings:HangfireConnection", $"{cs};Database=ECP_Hangfire");
        builder.UseSetting("Seed:CustomerCount", "5");
        builder.UseSetting("Seed:ProductCount", "5");
        builder.UseSetting("Seed:OrderCount", "5");
    }
}
```

4. Headline scenarios to assert:
   - login (`admin@ecom.com`) → `POST /api/orders` → product stock decremented;
     `POST /api/orders/{id}/cancel` → stock restored.
   - oversell rejected (order qty > stock → 400).
   - a forged-but-valid VNPay IPN → `Order.PaymentStatus = Paid` + auto-confirmed;
     replayed IPN is idempotent.
   - authorization: a customer gets 404 for another customer's order.
   - ETL run → `fact.SalesOrderItem` populated, Gold tables refreshed.

> Note: `Program` must be reachable to the test project. With top-level statements,
> add `public partial class Program { }` at the end of `Program.cs` (or expose via
> `InternalsVisibleTo`).
