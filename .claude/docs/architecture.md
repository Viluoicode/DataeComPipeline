# Architecture

## Clean Architecture — 4 projects

```
ECommerPipeline.Api            → HTTP entry (minimal API, JWT, SignalR, OTel)
        ↓ depends on
ECommerPipeline.Infrastructure → tech impl (EF Core, Dapper, Hangfire, BCrypt, ClosedXML)
        ↓ depends on
ECommerPipeline.Application     → interfaces, DTOs, FluentValidation (no tech)
        ↓ depends on
ECommerPipeline.Domain          → entities, enums (zero dependencies)
```

Dependency flow is inward only. Domain knows nothing about EF Core. Swapping a tech (e.g. ORM) touches only Infrastructure.

This is **pragmatic Clean Architecture (Jason Taylor style)**: `IOltpDbContext` in Application references EF Core abstractions (`DbSet<T>`) rather than wrapping a Repository pattern. Accepted trade-off.

## OLTP vs OLAP split (CQRS in practice)

- **Write path:** Api → `IOrderService` → `OrderService` (Infrastructure) → `OltpDbContext` (EF Core) → OLTP database (`dbo` schema, B-tree indexes).
- **Read path (analytics):** Api → `IReportService` → `ReportService` (Infrastructure) → Dapper raw SQL → OLAP database (Columnstore, Gold layer).
- Synced by `SalesEtlPipeline` (Hangfire). Eventual consistency (~5 min, or hourly on Azure).

## Project layout

```
src/
  ECommerPipeline.Domain/         Entities (Customer, Product, Order, OrderItem, RefreshToken), Enums (OrderStatus, UserRole)
  ECommerPipeline.Application/    Common/{Interfaces,DTOs}, Orders/, Customers/, Products/, Reports/, Import/, Auth/ (each: I*Service + DTOs + Validators)
  ECommerPipeline.Infrastructure/
    Auth/                         JwtTokenService, AuthService, JwtOptions
    Persistence/Oltp/             OltpDbContext, Configurations/, Migrations/
    Persistence/Olap/             OlapConnectionFactory, Scripts/OlapSchema.sql (embedded resource)
    Orders|Customers|Products|Reports/   services
    Import/                       ExcelImportService (ClosedXML)
    Etl/                          SalesEtlPipeline, EtlJob (+Polly), CompressColumnstoreJob, DataQualityJob
    DependencyInjection.cs        AddInfrastructure() + RegisterRecurringJobs()
  ECommerPipeline.Api/
    Program.cs                    DI wiring, JWT, OTel, CORS, all minimal-API endpoints
    Hubs/                         EtlNotificationHub, SignalREtlNotifier (IEtlNotifier impl)
    Middleware/                   GlobalExceptionHandler, CorrelationIdMiddleware
tests/                            Application.Tests (30), Infrastructure.Tests (18)
frontend/                         React SPA
```

## DI registration

`AddInfrastructure(config)` in `DependencyInjection.cs` registers: OltpDbContext (EF, `EnableRetryOnFailure`), OlapConnectionFactory (singleton), all services (scoped), ETL jobs (scoped), Hangfire (SQL storage). `IEtlNotifier` is implemented in the Api layer (`SignalREtlNotifier`) so Infrastructure doesn't depend on SignalR.

`RegisterRecurringJobs(sp)` schedules 3 Hangfire recurring jobs with config-driven cron (`Jobs:EtlCron`, `Jobs:CompressCron`, `Jobs:DataQualityCron`).

## Startup sequence (`DatabaseInitializer.InitializeAsync`)

1. `EnsureDatabasesExistAsync` — `CREATE DATABASE` if missing (skipped when `Database:AutoCreate=false`, e.g. Azure SQL).
2. `ApplyOltpMigrationsAsync` — EF `MigrateAsync`.
3. `ApplyOlapSchemaAsync` — runs embedded `OlapSchema.sql` (IF NOT EXISTS guards).
4. `SeedAsync` — admin + demo accounts, then Bogus data (sizes from `Seed:*` config). Skips if customers already exist.
