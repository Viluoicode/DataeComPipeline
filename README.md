# ECommerPipeline — Data Pipeline OLTP → OLAP

> Mô phỏng "nỗi đau" của một e-commerce thật: **báo cáo thống kê làm chậm DB bán hàng**.
> Giải pháp: tách 2 database (OLTP để ghi, OLAP để đọc) và viết ETL pipeline đồng bộ giữa chúng.

---

## 1. Project làm gì?

Một hệ thống e-commerce backend có **3 thành phần chính**:

| Thành phần | Trách nhiệm |
|---|---|
| **OLTP DB** (row-store) | Ghi đơn hàng tốc độ cao. Indexes tối ưu cho INSERT/UPDATE. |
| **OLAP DW** (Columnstore) | Star schema (fact + dimension). Phục vụ báo cáo phức tạp với JOIN/GROUP BY trên hàng triệu row trong <1s. |
| **ETL Pipeline** (Hangfire) | Mỗi 5 phút: extract dữ liệu mới từ OLTP, transform sang star schema, bulk-load vào OLAP. |

Demo nguyên lý: **CQRS thực tế** — đọc và ghi ở 2 nơi khác nhau, đồng bộ qua ETL.

---

## 2. Kiến trúc Clean Architecture

```
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Api   (Minimal API + Hangfire UI)      │
└─────────────────────────────────────────────────────────┘
                  ▲                       ▲
                  │                       │
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Infrastructure                         │
│  ├── Persistence/Oltp   (EF Core, write path)           │
│  ├── Persistence/Olap   (Dapper, read path, raw SQL)    │
│  ├── Etl                (SalesEtlPipeline + watermark)  │
│  └── Initialization     (DatabaseInitializer)           │
└─────────────────────────────────────────────────────────┘
                  ▲
                  │
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Application                            │
│  Interfaces, DTOs, contracts                            │
└─────────────────────────────────────────────────────────┘
                  ▲
                  │
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Domain                                 │
│  Entities, Enums (no deps)                              │
└─────────────────────────────────────────────────────────┘
```

---

## 3. Data Flow

```
                         ┌───────────────┐
   Client POST /orders ─►│  Core API     │
                         └───────┬───────┘
                                 │ EF Core (write)
                                 ▼
                    ┌─────────────────────────┐
                    │  OLTP — row-store DB    │
                    │  Customers / Products / │
                    │  Orders / OrderItems    │
                    └────────────┬────────────┘
                                 │
                  Hangfire job   │ extract delta (watermark)
                  every 5 min    │
                                 ▼
                    ┌─────────────────────────┐
                    │  ETL: SalesEtlPipeline  │
                    │  1. UPSERT dim.Customer │
                    │  2. UPSERT dim.Product  │
                    │  3. UPSERT dim.Date     │
                    │  4. SqlBulkCopy → fact  │
                    │  5. Update watermark    │
                    └────────────┬────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │  OLAP — Star Schema     │
                    │  CCI on fact.SalesOrderItem  │  ◄── Dapper raw SQL
                    │  dim.Date / dim.Customer/ │   (Reports API)
                    │  dim.Product            │
                    └─────────────────────────┘
                                 ▲
                                 │ GET /api/reports/*
                                 │
                         ┌───────┴───────┐
                         │  Dashboard /  │
                         │  BI tool      │
                         └───────────────┘
```

**Tại sao 2 DB?**
- OLTP cần index B-tree mỏng (INSERT nhanh)
- OLAP cần Columnstore (GROUP BY siêu nhanh, nén 10×) — nhưng INSERT chậm
- Chạy báo cáo phức tạp trực tiếp trên OLTP sẽ làm slow query log nổ → app sập

---

## 4. 📊 Benchmark — Bằng chứng định lượng

Cùng một báo cáo (3-way JOIN + GROUP BY trên doanh thu theo Category, 90 ngày):

| | OLAP (Columnstore) | OLTP (row-store) | Tỷ lệ |
|---|---|---|---|
| **Elapsed time** | **~90 ms** | **~1,200 ms** | OLAP nhanh hơn **~13×** |
| **Dataset**      | 300,229 rows       | 300,229 rows     | Cùng dataset, cùng query |
| **Khi scale 10M rows** | Vẫn ~100-200 ms | Vài chục giây → timeout | Khác biệt **càng lớn khi data càng lớn** |

### Compression ratio (sức mạnh của Columnstore)
| Format | Rows | Disk size | Compression |
|---|---|---|---|
| `fact.SalesOrderItem` raw row-store (ước tính) | 300,229 | ~50 MB | 1× |
| `fact.SalesOrderItem` CCI compressed | 300,229 | **~5 MB** | **~10×** |

### Bonus — Dashboard fetch reports (đo từ HTTP log)
| Trạng thái Columnstore | sales-by-day | top-products | sales-by-category |
|---|---|---|---|
| **OPEN** (delta store, chưa nén) | 4,963 ms | 4,999 ms | 5,035 ms |
| **COMPRESSED** (sau REORGANIZE) | ~90 ms | ~92 ms | ~95 ms |

→ Force compress tạo ra **~50× speedup** ở tầng API report. Đây chính là lý do project có 1 background job riêng tự động compress hàng đêm.

### Execution Plan — tại sao OLAP nhanh

Khi chạy query OLAP, SQL Server show:
- **Physical Operation:** `Columnstore Index Scan`
- **Actual Execution Mode:** `Batch` (thay vì Row Mode — đây là chế độ xử lý ~900 rows/batch, lý do chính OLAP nhanh)
- **Actual Number of Batches:** 334
- **Storage:** ColumnStore

> ⚠️ **Lưu ý quan trọng:** Columnstore chỉ phát huy khi rowgroup đã **COMPRESSED** (xem `sys.column_store_row_groups`). Với dataset < 100k rows, data nằm trong **delta store (OPEN)** → query thực chất chạy như row-store có overhead → **chậm hơn cả OLTP**. Phải `ALTER INDEX ... REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON)` hoặc đợi auto-compress khi đủ 1,048,576 rows/rowgroup.

### Cách reproduce
```sql
USE ECommerPipeline_Olap;
ALTER INDEX CCI_SalesOrderItem ON fact.SalesOrderItem
    REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);

SET STATISTICS TIME ON;
-- Chạy 3 lần, đo lần thứ 3 (warm cache)
SELECT  p.Category,
        COUNT(DISTINCT f.OrderId) AS OrderCount,
        SUM(f.LineTotal)          AS Revenue
FROM    fact.SalesOrderItem f
JOIN    dim.Product p ON p.ProductKey = f.ProductKey
JOIN    dim.Date    d ON d.DateKey    = f.DateKey
WHERE   d.[Date] >= DATEADD(DAY, -90, GETUTCDATE())
GROUP BY p.Category;
```

---

## 5. Tech Stack

### Backend
| Layer | Công nghệ |
|---|---|
| API | ASP.NET Core 9 Minimal API |
| OLTP DB | SQL Server 2022 (row-store) |
| OLAP DW | SQL Server 2022 (Clustered Columnstore Index) |
| Write ORM | Entity Framework Core 9 |
| Read ORM | Dapper (raw SQL, không track changes) |
| Bulk Load | `SqlBulkCopy` (5000 row/batch) |
| Job Scheduler | Hangfire (recurring + dashboard) |
| Real-time push | SignalR (notify Dashboard khi ETL xong) |
| Validation | FluentValidation |
| Resilience | Polly (retry transient SQL faults trong ETL) |
| Observability | Serilog → Console + Seq, Health Checks, OpenAPI |
| API Docs UI | Scalar |
| Seed | Bogus (5k customers, 1k products, 100k orders) |

### Frontend (SPA)
| Layer | Công nghệ |
|---|---|
| Framework | React 18 + TypeScript |
| Bundler | Vite |
| Charts | Recharts |
| HTTP | Axios |
| Real-time | `@microsoft/signalr` client |
| Routing | React Router |

---

## 6. Chạy như thế nào?

### Yêu cầu
- **.NET 9 SDK**
- **Node.js 20+** (cho frontend)
- **SQL Server** (LocalDB, Express, hoặc full) chạy ở `localhost`, Windows Authentication

### Bước 1 — Backend
```powershell
dotnet run --project src/ECommerPipeline.Api
```

Khi khởi động, app sẽ **tự động**:
1. Tạo 3 database (`ECommerPipeline_Oltp` / `_Olap` / `_Hangfire`) nếu chưa có
2. Apply EF migrations cho OLTP
3. Apply OLAP star schema (embedded SQL script)
4. Seed 5,000 customers + 1,000 products + 100,000 orders nếu OLTP rỗng (~5-10 phút lần đầu — có thể giảm trong `appsettings.json` → `Seed`)

### Bước 2 — Frontend (terminal khác)
```powershell
cd frontend
npm install
npm run dev
```

### URLs sau khi 2 service đều chạy
- **🎨 Dashboard SPA** (chính): http://localhost:5173
- **📚 Scalar API UI**: http://localhost:5193/scalar/v1
- **⚙️ Hangfire Dashboard**: http://localhost:5193/hangfire
- **❤️ Health Check**: http://localhost:5193/health
- **📋 OpenAPI JSON**: http://localhost:5193/openapi/v1.json

---

## 7. Test như thế nào?

Mở [`src/ECommerPipeline.Api/ECommerPipeline.Api.http`](src/ECommerPipeline.Api/ECommerPipeline.Api.http) trong VS Code (cần extension **REST Client** — `humao.rest-client`). Click **Send Request** lần lượt 7 block. Flow:

1. POST 3 đơn hàng (ghi OLTP)
2. POST `/api/admin/trigger-etl` (đẩy dữ liệu sang OLAP ngay)
3. GET 3 báo cáo (đọc từ OLAP/Columnstore)

Để test lại từ đầu: POST `/api/admin/reset`.

---

## 8. API Endpoints

| Method | Path | Tag | Mô tả |
|---|---|---|---|
| POST | `/api/orders` | Orders | Tạo đơn hàng — ghi vào OLTP (FluentValidation chặn input xấu) |
| GET | `/api/reports/sales-by-category?from&to` | Reports | Doanh thu theo category (đọc OLAP) |
| GET | `/api/reports/sales-by-day?from&to` | Reports | Doanh thu theo ngày |
| GET | `/api/reports/top-products?from&to&top` | Reports | Top N sản phẩm bán chạy |
| POST | `/api/admin/trigger-etl` | Admin | Enqueue ETL job vào Hangfire (async, 202 Accepted) |
| POST | `/api/admin/compress-columnstore` | Admin | Enqueue Force Compress columnstore job |
| POST | `/api/admin/reset` | Admin | Wipe orders + OLAP fact + watermark |
| GET | `/health` | — | Health check 2 DB |
| GET | `/hub/etl` | — | SignalR hub — push `etl-completed` event lên client |
| GET | `/hangfire` | — | Hangfire dashboard (xem job history) |
| GET | `/scalar/v1` | — | Scalar API documentation UI |

---

## 9. Điểm kỹ thuật nổi bật

| Vấn đề | Giải pháp trong project |
|---|---|
| Báo cáo làm chậm DB bán hàng | Tách OLTP / OLAP, ETL đồng bộ qua Hangfire |
| Aggregate trên triệu row chậm | **Clustered Columnstore Index** trên `fact.SalesOrderItem` (~10× compression) |
| ETL load lại toàn bảng tốn kém | **Watermark pattern**: `etl.Watermark` lưu `LastProcessedRowId`, chỉ extract delta |
| INSERT từng row chậm | **`SqlBulkCopy`** batch 5000 row/lần |
| Dimension thay đổi (SCD) | **Bulk staging + MERGE WITH (HOLDLOCK)** — UPSERT idempotent, race-condition safe |
| Job thất bại tạm thời (DB blip) | **Polly retry** 3 lần, exponential backoff + jitter |
| 2 ETL job chạy song song → conflict | `[DisableConcurrentExecution]` ở Hangfire job |
| Columnstore data mới nằm ở delta store → query chậm | **CompressColumnstoreJob** tự chạy 2AM hàng đêm: REORGANIZE WITH COMPRESS |
| User phải đợi ETL trên HTTP timeout | Endpoint `trigger-etl` trả `202 Accepted` ngay, ETL chạy ngầm Hangfire |
| User phải F5 để thấy data mới | **SignalR push** sau khi job ETL succeeded → Dashboard tự refresh |
| Validation logic rải rác | **FluentValidation** + global exception handler trả ProblemDetails |
| Logs khó debug khi distributed | Structured logging với **Serilog** → Console + Seq |
| Architecture lệ thuộc EF Core | Application layer ref EF Core (chấp nhận trade-off pragmatic của Jason Taylor's Clean Arch) |

---

## 9b. 🎨 Frontend Dashboard

SPA React + TypeScript hiển thị data real-time từ OLAP, có **3 page chính**:

### Dashboard (`/`)
- 3 KPI cards: **Total Revenue / Total Orders / Categories**
- **Sales by Day chart** (line chart, 2 trục: Revenue & Order Count)
- **Top Products** (bar chart)
- **Sales by Category** (pie chart)
- Date range filter (default: 90 ngày qua)
- Badge **SignalR status** (connected/reconnecting/disconnected)

### Stress Test (`/stress`)
Công cụ chứng minh OLTP write throughput + ETL pipeline:
- Input số orders → bấm **Fire** → bắn N requests song song qua `Promise.all`
- Progress bar realtime, hiển thị thời gian + throughput (orders/sec)
- Nút **Trigger ETL** (enqueue Hangfire job)
- Nút **Force Compress** (chạy CompressColumnstoreJob ngay không đợi 2AM)
- Sau khi job xong → SignalR push → Dashboard tự refresh

### Real-time qua SignalR
```typescript
// frontend/src/hooks/useEtlNotifications.ts
const conn = new HubConnectionBuilder()
    .withUrl('/hub/etl')
    .withAutomaticReconnect()
    .build();

conn.on('etl-completed', (evt) => {
    // Dashboard auto-refresh
    onEtlCompleted?.(evt);
});
```

Backend bắn event từ `SalesEtlPipeline` cuối mỗi run thành công → user không cần F5.

---

## 9c. 🗜️ Auto Columnstore Maintenance

**Vấn đề:** Columnstore SQL Server **chỉ auto-compress khi rowgroup đủ 1,048,576 rows**. Cho hệ thống low-volume (vài nghìn order/ngày), data nằm mãi trong **delta store (state = OPEN)** → query thực chất chạy row-based → chậm hơn cả OLTP.

**Giải pháp:** `CompressColumnstoreJob` chạy mỗi đêm 2AM (Hangfire cron `0 2 * * *`):
1. Query `sys.column_store_row_groups` đếm rowgroups OPEN/CLOSED
2. Nếu có rowgroups chưa nén → chạy `ALTER INDEX ... REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON)`
3. Re-check + log số rowgroups đã compress

Kết quả từ log thực tế:
```
Columnstore health: Open=1, Closed=0, Compressed=0, TotalRows=300229
Compressing 1 uncompressed row groups...
Compression done in 1938 ms. Now Open=0, Closed=0, Compressed=1
```

→ **OLAP queries từ 5,000ms (delta store) xuống ~90ms (compressed)** — speedup ~50×.

---

## 10. Cấu trúc thư mục

```
ECommerPipeline/
├── src/                                       ← Backend (.NET 9)
│   ├── ECommerPipeline.Domain/                ← Entities, Enums (no deps)
│   │
│   ├── ECommerPipeline.Application/           ← Interfaces, DTOs, Validators
│   │   ├── Common/Interfaces/
│   │   ├── Orders/{IOrderService, DTOs, Validators}
│   │   └── Reports/{IReportService, DTOs}
│   │
│   ├── ECommerPipeline.Infrastructure/
│   │   ├── DependencyInjection.cs
│   │   ├── Initialization/{DatabaseInitializer, ResetService}.cs
│   │   ├── Persistence/
│   │   │   ├── Oltp/ {OltpDbContext, Configurations, Migrations}
│   │   │   └── Olap/ {OlapConnectionFactory, Scripts/OlapSchema.sql}
│   │   ├── Orders/OrderService.cs
│   │   ├── Reports/ReportService.cs           ← raw SQL với Dapper
│   │   ├── Realtime/EtlNotificationHub.cs     ← SignalR hub
│   │   └── Etl/
│   │       ├── SalesEtlPipeline.cs            ← Extract→Transform→Load
│   │       ├── EtlJob.cs                      ← + Polly retry
│   │       └── CompressColumnstoreJob.cs      ← auto-compress 2AM
│   │
│   └── ECommerPipeline.Api/
│       ├── Program.cs                          ← minimal API + DI wiring
│       ├── Middleware/GlobalExceptionHandler.cs
│       ├── ECommerPipeline.Api.http            ← test flow đầy đủ
│       └── appsettings.json
│
├── frontend/                                   ← React SPA (Vite + TypeScript)
│   ├── src/
│   │   ├── pages/{Dashboard, StressTest}.tsx
│   │   ├── components/{KpiCard, charts/...}.tsx
│   │   ├── hooks/useEtlNotifications.ts        ← SignalR client
│   │   ├── api/{client, reports, admin}.ts     ← Axios calls
│   │   └── App.tsx
│   ├── package.json
│   └── vite.config.ts                          ← proxy to backend
│
└── ECommerPipeline.sln
```

---

## 11. Screenshots

> 📸 **TODO:** Đính kèm 4 ảnh sau (sau khi bạn chụp):
>
> 1. `docs/screenshots/dashboard.png` — Dashboard với 22T VND revenue + biểu đồ 90 ngày
> 2. `docs/screenshots/stress-test.png` — Fire 1000 orders + Hangfire processing kế bên
> 3. `docs/screenshots/hangfire.png` — Recurring Jobs (`sales-etl` + `compress-columnstore`)
> 4. `docs/screenshots/execution-plan.png` — SSMS Execution Plan: Columnstore Index Scan, Batch Mode

---

## 12. Hướng phát triển tiếp (v2 roadmap)

### Done ✅
- [x] SignalR — push báo cáo realtime lên dashboard
- [x] Frontend SPA — React Dashboard + Stress Test
- [x] Automated Columnstore Maintenance Job

### Đang muốn làm
- [ ] **CDC (Change Data Capture)** thay watermark — track DELETE/UPDATE chính xác (cần SQL Server Developer/Enterprise + sysadmin role)
- [ ] **Partitioning** theo tháng cho `fact.SalesOrderItem` — chuẩn bị khi lên 100M+ rows
- [ ] **MassTransit/RabbitMQ** — async event-driven thay vì polling 5 phút (near real-time ETL)
- [ ] **SCD Type 2** cho `dim.Customer`/`dim.Product` — giữ lịch sử thay đổi thay vì overwrite
- [ ] **Power BI / Grafana** đọc trực tiếp OLAP — share dashboard với non-technical user
- [ ] **xUnit + Testcontainers** — integration test với SQL Server container
- [ ] **Docker compose** — 1 lệnh up cả stack: SQL Server + Seq + API + Frontend
