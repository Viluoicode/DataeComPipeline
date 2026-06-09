<h1 align="center">🛒 ECommerPipeline</h1>

<p align="center">
  <strong>Full-stack e-commerce với data pipeline OLTP → ETL → OLAP.</strong><br/>
  Tách database ghi (OLTP) và đọc (OLAP), đồng bộ qua ETL, dashboard real-time qua SignalR.
</p>

<p align="center">
  <img src="https://img.shields.io/github/actions/workflow/status/Viluoicode/DataeComPipeline/ci.yml?branch=main&label=CI&logo=github" alt="CI"/>
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/React-18-61DAFB?logo=react&logoColor=black" alt="React 18"/>
  <img src="https://img.shields.io/badge/SQL_Server-2022-CC2927?logo=microsoftsqlserver&logoColor=white" alt="SQL Server"/>
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white" alt="Docker"/>
  <img src="https://img.shields.io/badge/tests-48_passing-success?logo=xunit" alt="48 tests"/>
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT"/>
</p>

---

## High-level Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  React 18 + TypeScript + Tremor (Vite)                       │
│  Storefront (/)  ·  Admin BI dashboard (/admin)              │
└──────────────────────────┬───────────────────────────────────┘
                REST + JWT  │  SignalR (real-time)
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  ASP.NET Core 9 — Clean Architecture                         │
│  JWT auth · FluentValidation · Polly · OpenTelemetry         │
└───────────┬───────────────────────────────┬──────────────────┘
   EF Core   │ (write)             (read) │ Dapper
            ▼                               ▼
   ┌─────────────────┐  ETL (Hangfire) ┌──────────────────────────┐
   │ OLTP (row-store)│ ──────────────► │  OLAP (Medallion)        │
   │ Orders,         │  watermark +    │  🥉 Bronze → 🥈 Silver   │
   │ Customers,      │  SqlBulkCopy +  │  (Columnstore) → 🥇 Gold │
   │ Products        │  SCD Type 2     │  + Data Quality (11 tests)│
   └─────────────────┘                 └────────────┬─────────────┘
                                       NL→SQL (read) │ analyst_ro
                                                    ▼
                              ┌──────────────────────────────────────┐
                              │  AI Data Analyst (.NET 10, :8090)     │
                              │  VN/EN question → AST-validated SELECT │
                              │  on Gold → rows + NL summary           │
                              └──────────────────────────────────────┘
```

> **Vì sao tách OLTP/OLAP?** Báo cáo phân tích phức tạp (JOIN/GROUP BY trên triệu rows) làm chậm DB bán hàng. Tách 2 store: OLTP dùng B-tree index (ghi nhanh), OLAP dùng Columnstore (đọc nhanh), đồng bộ qua ETL. Đây là **CQRS thực tế**.

---

## Quick Start (Docker)

```bash
git clone https://github.com/Viluoicode/DataeComPipeline.git
cd DataeComPipeline
docker compose up -d          # SQL Server + API + Frontend + Jaeger (~5 phút lần đầu)
```

Mở **http://localhost** → login → shop → đặt đơn → xem analytics.

**Demo accounts** (seed sẵn):

| Role | Email | Password |
|---|---|---|
| 👑 Admin | `admin@ecom.com` | `admin123` |
| 🛒 Customer | `demo@ecom.com` | `demo123` |

**URLs:** `/` storefront · `/admin` dashboard · `/scalar/v1` API docs · `/hangfire` jobs · `localhost:16686` Jaeger tracing · **`localhost:8090` AI Data Analyst (NL→SQL)**

> Local dev (hot reload): `dotnet run --project src/ECommerPipeline.Api` + `cd frontend && npm run dev` → http://localhost:5173

---

## Features

- **🛍 Storefront** — browse 100+ products, cart, checkout, order history, JWT login/register
- **🎛 Admin BI** — dashboard (KPI + charts real-time qua SignalR), orders CRUD + filter, Excel import, stress-test tool
- **🏗 Data Engineering** — Medallion (Bronze/Silver/Gold), SCD Type 2 dimensions, watermark ETL, 11 data-quality tests, auto Columnstore compression, OpenTelemetry tracing
- **🤖 AI Data Analyst** — "Ask Data" chat ngay trong Admin (`/admin/ask`): câu hỏi NL (VN/EN) → safe read-only SQL trên Gold layer; T-SQL AST validation + schema whitelist + least-privilege DB principal (`ai-analyst/`)
- **🔒 Production-grade** — JWT (access + refresh), Polly retry, structured logging + correlation ID, 48 unit tests, GitHub Actions CI

---

## Performance

Cùng query "doanh thu theo category 90 ngày" trên **300k rows**:

| Layer | Latency | Speedup |
|---|---|---|
| OLTP (row-store, 3-way JOIN) | ~1,200 ms | baseline |
| OLAP Silver (Columnstore compressed) | ~90 ms | **13×** |
| OLAP Gold (pre-aggregated) | ~5-10 ms | **~150×** |

Compression: **~10×** (50MB → 5MB). Chi tiết benchmark + execution plan → [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Tech Stack

| Layer | Technologies |
|---|---|
| **Backend** | ASP.NET Core 9, EF Core 9 (OLTP), Dapper (OLAP), Hangfire, SignalR, JWT Bearer, BCrypt, FluentValidation, Polly, Serilog, OpenTelemetry, ClosedXML |
| **Database** | SQL Server 2022 (row-store + Clustered Columnstore Index) |
| **Frontend** | React 18, TypeScript, Vite, Tremor + Tailwind CSS, Recharts, Axios, SignalR client |
| **DevOps** | Docker Compose, Nginx, GitHub Actions, Jaeger |
| **Testing** | xUnit, Moq, FluentAssertions, EF Core InMemory, Coverlet |

---

## Testing

```bash
dotnet test          # 48 tests (30 Application + 18 Infrastructure)
```

Test **business logic quan trọng**: validators, order-total computation, JWT, BCrypt, ETL date-key transform, pagination — không test getter/setter linh tinh.
CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)): build + test + docker-build mỗi push/PR.

---

## Security & Secret Management

Secret được tách rõ giữa **dev** và **production** — **không có secret thật nào trong repo**:

| Loại | Cách xử lý |
|---|---|
| **API keys** (OpenAI/Azure) | Không commit — để trống trong `appsettings.json`, nạp qua env var / user-secrets khi cần |
| **Connection string + JWT secret (prod)** | Nạp qua biến môi trường / Azure config. `docker-compose.prod.yml` đọc từ `.env` (đã `.gitignore`) |
| **`appsettings.json`** | Không chứa password literal — runtime inject qua `ConnectionStrings__*` env var hoặc user-secrets |
| **Dev/demo creds** | Mật khẩu trong `docker-compose.yml` là **throwaway** cho SQL Server chạy localhost (không expose internet). Production **từ chối khởi động** nếu còn dùng JWT dev secret |
| **Least privilege** | AI Analyst chạy bằng principal **`analyst_ro`** chỉ có quyền `SELECT` trên schema Gold; SQL sinh ra được validate AST (1 `SELECT` + whitelist bảng/cột) trước khi chạy |

> Config precedence: `appsettings.json` < user-secrets *(dev)* < environment variables *(prod)*.

---

## Documentation

| Doc | Nội dung |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Enterprise patterns: Medallion, SCD Type 2, Data Quality, OpenTelemetry — + so sánh enterprise reality |
| [STUDY_GUIDE.md](docs/STUDY_GUIDE.md) | Deep-dive 10 tech stack (giải thích + Q&A phỏng vấn + self-test) |
| [DOCKER.md](docs/DOCKER.md) | Docker quickstart + troubleshooting |
| [DEPLOY_VPS.md](docs/DEPLOY_VPS.md) | **Deploy lên VPS (always-on, HTTPS qua Caddy)** — docker-compose.prod.yml, từng bước |
| [DEPLOY_AZURE.md](docs/DEPLOY_AZURE.md) | Deploy lên Azure free tier (App Service + Azure SQL) |
| [AI_ANALYST_INTEGRATION.md](docs/AI_ANALYST_INTEGRATION.md) | NL→SQL layer (`ai-analyst/`): schema whitelist, read-only principal, docker wiring, safety model |
| [DECISIONS.md](docs/DECISIONS.md) | Architecture decisions & trade-offs (ADR) — vì sao mỗi lựa chọn + "nếu scale thì đổi gì" |
| [CHANGELOG.md](docs/CHANGELOG.md) | Hành trình phát triển theo phase + metrics |

---

## Key Technical Decisions

| Quyết định | Lý do |
|---|---|
| Tách OLTP/OLAP | B-tree (write) vs Columnstore (read) xung khắc → 2 store, đồng bộ ETL |
| EF Core (write) + Dapper (read) | EF: migration + type-safe; Dapper: raw SQL nhanh hơn cho analytical |
| Watermark ETL (thay CDC) | CDC cần SQL Enterprise; watermark đủ cho demo, idempotent + resumable |
| SCD Type 2 dimensions | Báo cáo lịch sử show đúng customer state lúc đặt đơn (không overwrite) |
| Medallion Bronze/Silver/Gold | Bronze = replay/audit; Silver = star schema; Gold = pre-aggregated (5ms) |
| JWT stateless + refresh rotation | Scale tốt; refresh token DB-stored để revoke được |

---

## Roadmap

**Done:** OLTP/OLAP split · Medallion · SCD Type 2 · Data Quality · OpenTelemetry · JWT auth · 48 tests · CI · Docker
**Next:** CDC (thay watermark) · Kafka event-driven ETL · table partitioning · Testcontainers integration tests · Azure deploy

---

## License

[MIT](LICENSE) — learning / portfolio project.
