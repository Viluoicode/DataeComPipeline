# Changelog — Hành trình build ECommerPipeline

> Đây là nhật ký phát triển chi tiết từ "scaffold trống" → "demo full-stack có Docker".
> Đọc cái này để hiểu **vì sao** mỗi feature được thêm vào, không chỉ **cái gì** đã thay đổi.

---

## v1.0 — Production-ready demo

### Tóm tắt 1 dòng
> Một project ASP.NET Core 9 + React 18 demo kiến trúc OLTP/OLAP với ETL pipeline, đầy đủ storefront + admin dashboard + benchmark + Docker.

### Tổng quan numbers
| Metric | Value |
|---|---|
| Backend projects | 4 (Domain, Application, Infrastructure, Api) |
| Backend endpoints | 25+ |
| Background jobs | 2 (sales-etl, compress-columnstore) |
| Frontend pages | 11 (storefront + admin) |
| Lines of code (backend) | ~3,500 |
| Lines of code (frontend) | ~3,800 |
| Docker containers | 3 (sql + api + frontend) |
| Git commits | 12 |
| Time to dev | ~2 tuần intensive |

---

## Các phase đã hoàn thành

### 🏛 Phase 1 — Foundation (commit `31a8ed1`)
**Mục tiêu:** Setup Clean Architecture với OLTP/OLAP split + ETL pipeline.

- ✅ 4 project Clean Architecture: Domain → Application → Infrastructure → Api
- ✅ OLTP database (SQL Server row-store) với EF Core 9
  - Entities: Customer, Product, Order, OrderItem
  - Migrations + indexes (OrderDate, CustomerId+OrderDate, Sku, Email unique)
- ✅ OLAP database (SQL Server với Clustered Columnstore Index)
  - Star schema: dim.Customer, dim.Product, dim.Date, fact.SalesOrderItem
  - etl.Watermark cho incremental load
- ✅ ETL pipeline `SalesEtlPipeline`:
  - Watermark pattern (chỉ extract delta)
  - `SqlBulkCopy` 5000 row/batch
  - MERGE WITH HOLDLOCK cho dimensions (race-condition safe)
  - Polly retry 3 lần exponential backoff
  - `[DisableConcurrentExecution]` chống concurrent runs
- ✅ Hangfire scheduler 2 recurring jobs:
  - `sales-etl`: `*/5 * * * *` (every 5 min)
  - `compress-columnstore`: `0 2 * * *` (every night 2AM)
- ✅ Background `CompressColumnstoreJob` — REORGANIZE WITH COMPRESS_ALL_ROW_GROUPS
- ✅ Reports API (Dapper raw SQL trên OLAP):
  - sales-by-category, sales-by-day, top-products
- ✅ SignalR hub `EtlNotificationHub` push event khi ETL xong
- ✅ FluentValidation cho `CreateOrderRequest`
- ✅ Global exception handler trả ProblemDetails
- ✅ Health checks 2 DB
- ✅ Scalar API docs UI
- ✅ Serilog → Console + Seq optional
- ✅ Bogus seeder 5k customers / 1k products / 100k orders

### 📦 Phase 2 — Order Management + Excel Import (commit `d0b1128`, `a65c57e`)
**Mục tiêu:** Bỏ phụ thuộc SSMS — admin tạo/quản lý order qua UI.

**Backend:**
- ✅ `IOrderService.GetPagedAsync` (paginated + filter status/date/customer/search)
- ✅ `IOrderService.GetByIdAsync` (chi tiết + line items)
- ✅ `ICustomerService.SearchAsync` (lookup cho form)
- ✅ `IProductService.SearchAsync + GetCategoriesAsync` (lookup + categories)
- ✅ `IImportService` với ClosedXML:
  - Import Customers (FullName, Email, Phone, City)
  - Import Products (Sku, Name, Category, Brand, Price, Stock)
  - Import Orders (gom theo OrderRef column)
  - Per-row validation, collect errors thay vì fail-fast
  - Template download endpoint
- ✅ 8 endpoints mới: /api/orders (list+detail), /api/customers, /api/products (+categories), /api/import/* (3 types + template)

**Frontend (Tremor refactor):**
- ✅ Cài Tailwind CSS + Tremor + Heroicons
- ✅ Refactor toàn bộ UI dùng Tremor components (Card, Metric, AreaChart, DonutChart, BarList, Table, Badge)
- ✅ AppLayout với sidebar BI-style
- ✅ Page mới: OrdersList (filter + pagination), OrderDetail, CreateOrder (3-step form), ImportPage (3 tabs upload)

### 🛍 Phase 3 — Public Storefront (commit `ca63110`)
**Mục tiêu:** Customer-facing experience — biến tool thành "shop"

- ✅ React Router với 2 layout: PublicLayout + AppLayout
- ✅ AuthContext + CartContext (localStorage-backed)
- ✅ Pages mới:
  - **/** Landing với hero + feature grid + architecture diagram
  - **/shop** Grid 100+ products với image (Picsum), category filter, debounced search
  - **/shop/:id** Product detail với qty selector + related products
  - **/checkout** 2-column form với sticky order summary
  - **/my-orders** Order history của user đang login
  - **/login** Mock — search customer trong DB
  - **/register** Mock — verify email tồn tại
- ✅ CartDrawer (slide-in từ phải)
- ✅ Admin pages move sang `/admin/*`
- ✅ react-hot-toast cho notifications
- ✅ ConfirmDialog component (thay browser confirm())
- ✅ lib/format.ts: formatVnd, formatCompact, productImage

### 🎨 Phase 4 — UI Contrast + UX Polish (commit `2907f20`)
**Mục tiêu:** Dark mode đúng, text readable, không bị "tối tối khó nhìn".

- ✅ Set `dark` class trên `<html>` (không phải inner div) — Tailwind dark variant work toàn bộ
- ✅ Override Tremor content colors với gray-50/100/200/300 trên dark bg
- ✅ Body bg-gray-950 + text-gray-100 default
- ✅ Active sidebar item với blue ring emphasis
- ✅ Cards: bg-gray-900 + border-gray-800

### 🐛 Phase 5 — Bug Fixes (commits `4e5fa43`, `6da24c2`, `c367f6a`, `d66ba83`)
**Mục tiêu:** Production-grade error handling.

- ✅ `SqlException` cancellation → convert sang `OperationCanceledException`
- ✅ `GlobalExceptionHandler` xử lý OCE → return 499 (client closed request)
- ✅ Serilog demote 499 từ ERR xuống Debug
- ✅ EF Core `EnableRetryOnFailure` 3 lần exponential backoff
- ✅ Frontend `isAbortError(e)` helper + axios interceptor
- ✅ `AbortController` trong Dashboard useEffect
- ✅ `catch (OperationCanceledException) { throw; }` ở tất cả service methods (VS debugger ack)
- ✅ Dapper records — `CAST(... AS BIGINT/DECIMAL)` chống type mismatch

### 🐳 Phase 6 — Docker Stack (commits `8997c1b`, `c0aac9f`)
**Mục tiêu:** `docker compose up` chạy full stack trong 1 lệnh.

- ✅ Multi-stage Dockerfile cho API (sdk → aspnet runtime, non-root user)
- ✅ Frontend Dockerfile (Vite build → nginx)
- ✅ nginx.conf:
  - Reverse proxy /api → api:8080
  - WebSocket upgrade cho /hub (SignalR)
  - SPA fallback (try_files)
  - Gzip + cache static
- ✅ docker-compose.yml:
  - SQL Server 2022 với persistent volume + healthcheck (sqlcmd)
  - API với health endpoint check (curl)
  - Frontend trên port 80
  - `depends_on: service_healthy` đúng thứ tự startup
  - Env vars override connection strings + seed sizes
- ✅ CORS đọc `Cors__AllowedOrigins` từ env (defaults: Vite dev + Docker)
- ✅ .dockerignore
- ✅ docs/DOCKER.md hướng dẫn

---

## Roadmap chưa làm

### 🎯 Portfolio Polish (1-2 tuần)
- [ ] **P2** — Real JWT auth (replace mock): register hash password, login issue token, refresh, frontend axios interceptor
- [ ] **P3** — Public deployment (Render/Railway/Fly.io) với public URL
- [ ] **P4** — README rewrite với hero image, demo GIF, badges
- [ ] **P5** — Demo video 2-3 phút walkthrough
- [ ] **P6** — Critical UX: 404 page, empty states với illustration, mobile responsive
- [ ] **P7** — GitHub repo polish (topics, social preview, CI badge)

### 🚀 Future product features (sau khi xin việc xong)
- [ ] CDC (Change Data Capture) thay watermark — track DELETE/UPDATE
- [ ] Partitioning theo tháng cho `fact.SalesOrderItem`
- [ ] MassTransit/RabbitMQ — async event-driven ETL
- [ ] SCD Type 2 cho dim.Customer/dim.Product
- [ ] Power BI / Grafana integration
- [ ] xUnit + Testcontainers tests
- [ ] Payment integration (VNPay/Momo)
- [ ] Real product image upload (S3/Azure Blob)
- [ ] Multi-tenant architecture
- [ ] Internationalization (i18n) VN/EN

---

## Performance achievements

| Metric | Result | Method |
|---|---|---|
| OLAP query (Columnstore compressed) | ~90 ms / 300k rows | CCI + REORGANIZE WITH COMPRESS |
| OLTP query (row-store 3-way JOIN) | ~1,200 ms / 300k rows | Standard B-tree indexes |
| OLAP vs OLTP speedup | **~13×** | Same dataset, same query |
| Compression ratio | **~10×** (50MB → 5MB) | Columnstore native |
| Dashboard fetch (after compress) | **~50× faster** (5000ms → 90ms) | Force compress nightly |
| OLTP write throughput | 500+ orders/sec | EF Core batched inserts, B-tree indexes |
| ETL throughput | 5000 rows/batch | SqlBulkCopy + watermark |
