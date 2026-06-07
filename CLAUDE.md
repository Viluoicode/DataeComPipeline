# CLAUDE.md

High-level index for the ECommerPipeline codebase. Detailed technical docs live in `.claude/docs/`.

## 1. Project Overview

ECommerPipeline is a full-stack e-commerce app that demonstrates an OLTP → ETL → OLAP data pipeline. Orders are written to a row-store OLTP database (EF Core), an incremental ETL job (Hangfire) syncs them into a Columnstore OLAP database structured as a Medallion architecture (Bronze/Silver/Gold) with SCD Type 2 dimensions, and a React admin dashboard reads pre-aggregated analytics in real time (SignalR). A customer-facing storefront sits on top of the same backend.

An **AI Data Analyst** service (`ai-analyst/`, separate .NET 10 service) sits on top of the Gold layer as a natural-language → SQL query layer: it turns VN/EN questions into safe, read-only SQL (T-SQL AST validation + whitelist + a `analyst_ro` least-privilege DB principal) and runs them against the Gold tables. It is wired into the root `docker-compose.yml` as `analyst-api` (port 8090). See `ai-analyst/CLAUDE.md` for its internals; integration details in `docs/AI_ANALYST_INTEGRATION.md`.

## 2. Tech Stack

- **Backend:** ASP.NET Core 9 (minimal API), C# 13, Clean Architecture (Domain/Application/Infrastructure/Api)
- **Data:** SQL Server 2022; EF Core 9.0.0 (OLTP writes), Dapper 2.1 (OLAP reads, raw SQL)
- **Jobs/Realtime:** Hangfire 1.8 (SQL storage), SignalR
- **Auth:** JWT Bearer (Microsoft.AspNetCore.Authentication.JwtBearer 9.0.0), BCrypt.Net-Next
- **Cross-cutting:** FluentValidation, Polly (Microsoft.Extensions.Resilience), Serilog, OpenTelemetry 1.9 (→ Jaeger), ClosedXML
- **Frontend:** React 18 + TypeScript, Vite, Tremor + Tailwind CSS, Axios, @microsoft/signalr, React Router 6
- **DevOps:** Docker Compose (SQL + API + Frontend + Jaeger), Nginx, GitHub Actions
- **Tests:** xUnit, Moq, FluentAssertions 6.12, EF Core InMemory, Coverlet (48 tests)

## 3. Dev Commands

```bash
# Full stack (recommended)
docker compose up -d                 # SQL + API + Frontend + Jaeger
docker compose logs -f api           # watch logs
docker compose down -v               # stop + WIPE db (needed after OLAP schema changes)

# Local dev (hot reload) — do NOT run while Docker stack is up (port conflict)
dotnet run --project src/ECommerPipeline.Api      # backend → :5193
cd frontend && npm run dev                         # frontend → :5173

# Build & test
dotnet build
dotnet test                          # 48 tests
cd frontend && npm run build         # tsc + vite build
```

Demo accounts (seeded): `admin@ecom.com` / `admin123` (Admin), `demo@ecom.com` / `demo123` (Customer).

## 4. Core Logic Summary

The heart of the system is `SalesEtlPipeline.RunAsync` (Infrastructure/Etl). Each run:
1. Reads a **watermark** (last processed OrderItemId) from `etl.Watermark` — only delta is extracted.
2. **Upserts dimensions** with SCD Type 2 (SHA-256 hash change detection; close old version + insert new).
3. Loops batches of 5000: extract from OLTP → load **Bronze** (raw) + **Silver** (fact, Columnstore) via `SqlBulkCopy` inside one transaction → advance watermark.
4. Refreshes **Gold** pre-aggregated tables (truncate + repopulate).
5. Pushes a SignalR `etl-completed` event. A separate `DataQualityJob` runs 11 checks; `CompressColumnstoreJob` compresses rowgroups nightly.

Reports (`ReportService`, Dapper) query the **Gold** layer (~5-10ms), not the raw fact.

Full detail: `.claude/docs/etl-pipeline.md`.

## 5. Key Constraints

- **OLTP vs OLAP separation is the point.** Writes go through EF Core to OLTP (`dbo`); analytical reads go through Dapper to OLAP (`bronze`/`dim`/`fact`/`gold`/`etl`/`dq` schemas). Don't merge these paths.
- **SQL batch gotcha:** never `ALTER TABLE ADD column` then reference it in the same batch (compile-time failure). Add columns in `CREATE TABLE`. Pass transactions to Dapper via named arg `transaction: tx` (3rd positional is NOT transaction).
- **OLAP schema is `IF NOT EXISTS`-guarded** in `OlapSchema.sql` — changing a table's columns requires a fresh DB (`docker compose down -v`), it won't ALTER existing tables.
- **Columnstore needs COMPRESSED rowgroups** to be fast; new data sits in delta store (slow) until `REORGANIZE WITH COMPRESS`.
- **Cancellation is not an error:** `OperationCanceledException`/`TaskCanceledException` → HTTP 499, swallowed on frontend. Don't log as error.
- **Local vs Azure config is env-driven** (`Database:AutoCreate`, `Jobs:*Cron`, `Seed:*`). Don't hardcode; keep defaults working for local/Docker.
- **Never run Docker stack + local `dotnet run` simultaneously** (port 5193/1433/80 conflict).

## 6. Additional Documentation

- `.claude/docs/architecture.md` — Clean Architecture layers, dependency flow, project structure
- `.claude/docs/etl-pipeline.md` — ETL internals: watermark, Medallion, SCD Type 2, Gold refresh, Data Quality
- `.claude/docs/auth.md` — JWT access/refresh flow, role-based authorization, SignalR auth
- `.claude/docs/frontend.md` — React routing, contexts (Auth/Cart), SignalR hook, API client interceptors
- `.claude/docs/ai-analyst.md` — pipeline-side NL→SQL integration: `/api/ask` proxy, rate-limit/cache/metrics, schema whitelist, `analyst_ro`, safety constraints

Human-facing docs (recruiters, study) live separately in `docs/` (ARCHITECTURE, STUDY_GUIDE, DOCKER, DEPLOY_AZURE, CHANGELOG).
