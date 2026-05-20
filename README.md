# ECommerPipeline — Data Pipeline OLTP → OLAP

> Mô phỏng "nỗi đau" của một e-commerce thật: **báo cáo thống kê làm chậm DB bán hàng**.
> Giải pháp: tách 2 database (OLTP để ghi, OLAP để đọc) và viết ETL pipeline đồng bộ giữa chúng.

---

## 1. Project làm gì?

Một hệ thống e-commerce **fullstack** với **4 thành phần chính**:

| Thành phần | Trách nhiệm |
|---|---|
| **OLTP DB** (row-store) | Ghi đơn hàng tốc độ cao. Indexes tối ưu cho INSERT/UPDATE. |
| **OLAP DW** (Columnstore) | Star schema (fact + dimension). Phục vụ báo cáo phức tạp với JOIN/GROUP BY trên hàng triệu row trong <1s. |
| **ETL Pipeline** (Hangfire) | Mỗi 5 phút: extract dữ liệu mới từ OLTP, transform sang star schema, bulk-load vào OLAP. Mỗi đêm 2AM tự compress columnstore rowgroups. |
| **BI Dashboard** (React + Tremor) | SPA với 5 page: Dashboard real-time, Orders list/detail, Create Order form, Excel Import, Stress Test. SignalR push update khi ETL xong. |

Demo nguyên lý: **CQRS thực tế** — đọc và ghi ở 2 nơi khác nhau, đồng bộ qua ETL.
**Full end-to-end demo không cần SSMS** — tạo customer/product/order, import Excel, xem analytics đều qua UI.

---

## 2. Kiến trúc Clean Architecture

```
┌─────────────────────────────────────────────────────────┐
│  frontend/  (React + TypeScript + Tremor SPA)           │
│  Dashboard / Orders / CreateOrder / Import / StressTest │
└──────────────────────────┬──────────────────────────────┘
                           │ REST + SignalR
                           ▼
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Api   (Minimal API + Hangfire + SignalR)│
└─────────────────────────────────────────────────────────┘
                  ▲                       ▲
                  │                       │
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Infrastructure                         │
│  ├── Persistence/Oltp   (EF Core, write path)           │
│  ├── Persistence/Olap   (Dapper, read path, raw SQL)    │
│  ├── Etl                (SalesEtlPipeline + watermark)  │
│  │                       + CompressColumnstoreJob)      │
│  ├── Orders / Customers / Products  (services)          │
│  ├── Import             (ExcelImportService, ClosedXML) │
│  └── Initialization     (DatabaseInitializer)           │
└─────────────────────────────────────────────────────────┘
                  ▲
                  │
┌─────────────────────────────────────────────────────────┐
│  ECommerPipeline.Application                            │
│  Interfaces, DTOs, Validators, contracts                │
│  Orders / Customers / Products / Reports / Import       │
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
| Excel Import | ClosedXML (parse + generate .xlsx) |
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
| UI Library | **Tremor** + Tailwind CSS (BI-style components) |
| Icons | Heroicons |
| Charts | Tremor charts (AreaChart, DonutChart, BarList — Recharts under the hood) |
| HTTP | Axios |
| Real-time | `@microsoft/signalr` client |
| Routing | React Router v6 (nested layouts) |

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

### Cách 1 — Qua UI (khuyến nghị, không cần SSMS)

Mở http://localhost:5173, demo end-to-end qua sidebar:

| Bước | Page | Hành động |
|---|---|---|
| 1 | **New Order** | Search customer → chọn → add 3-4 products vào cart → Create |
| 2 | **Orders** | Filter status/date, paginate, click row xem OrderDetail |
| 3 | **Import Excel** | Download template → điền Excel → upload → xem success/error rows |
| 4 | **Stress Test** | Fire 1000 orders → Trigger ETL → Force Compress |
| 5 | **Dashboard** | SignalR push tự refresh KPIs + 3 charts khi ETL xong |

### Cách 2 — Qua REST file (cho người thích CLI)

Mở [`src/ECommerPipeline.Api/ECommerPipeline.Api.http`](src/ECommerPipeline.Api/ECommerPipeline.Api.http) trong VS Code (cần extension **REST Client** — `humao.rest-client`). Click **Send Request** lần lượt. Flow:

1. POST 3 đơn hàng (ghi OLTP)
2. POST `/api/admin/trigger-etl` (đẩy dữ liệu sang OLAP ngay)
3. GET 3 báo cáo (đọc từ OLAP/Columnstore)

Để test lại từ đầu: POST `/api/admin/reset` (hoặc click Reset Data trong Stress Test page).

---

## 8. API Endpoints

| Method | Path | Tag | Mô tả |
|---|---|---|---|
| **Orders (write + query)** | | | |
| POST | `/api/orders` | Orders | Tạo đơn hàng — ghi vào OLTP (FluentValidation chặn input xấu) |
| GET | `/api/orders?page&pageSize&status&customerId&from&to&search` | Orders | List có pagination + filter |
| GET | `/api/orders/{id}` | Orders | Chi tiết đơn + line items |
| **Customers / Products lookup** | | | |
| GET | `/api/customers?search&page&pageSize` | Customers | Lookup cho form Create Order |
| GET | `/api/products?search&category&page&pageSize` | Products | Lookup product với filter category |
| GET | `/api/products/categories` | Products | Danh sách category distinct |
| **Reports (OLAP, Columnstore)** | | | |
| GET | `/api/reports/sales-by-category?from&to` | Reports | Doanh thu theo category |
| GET | `/api/reports/sales-by-day?from&to` | Reports | Doanh thu theo ngày |
| GET | `/api/reports/top-products?from&to&top` | Reports | Top N sản phẩm bán chạy |
| **Excel Import** | | | |
| POST | `/api/import/customers` (multipart) | Import | Bulk import customers từ .xlsx |
| POST | `/api/import/products` (multipart) | Import | Bulk import products |
| POST | `/api/import/orders` (multipart) | Import | Bulk import orders (grouped by OrderRef) |
| GET | `/api/import/template/{kind}` | Import | Download template .xlsx (customers/products/orders) |
| **Admin / utility** | | | |
| POST | `/api/admin/trigger-etl` | Admin | Enqueue ETL job vào Hangfire (async, 202 Accepted) |
| POST | `/api/admin/compress-columnstore` | Admin | Enqueue Force Compress columnstore job |
| POST | `/api/admin/reset` | Admin | Wipe orders + OLAP fact + watermark |
| **Infra / docs** | | | |
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

## 9b. 🎨 Frontend SPA — BI Tool Look với Tremor

SPA React + TypeScript + **Tremor** (Tailwind CSS), layout sidebar dạng BI tool thật, **5 page**:

### `/` — Dashboard
- 3 KPI cards: **Total Revenue / Total Orders / Categories** (Tremor `Card` + `Metric`)
- **Sales by Day** — Tremor `AreaChart` (2-axis: Revenue M VND + Order count)
- **Sales by Category** — Tremor `DonutChart`
- **Top 10 Products** — Tremor `BarList` (ranked by revenue)
- Date range filter (default 90 ngày qua)
- Badge **SignalR status** (connected/reconnecting/disconnected) — refresh auto khi ETL xong

### `/orders` — Orders List
- Paginated table với search, status filter, date range filter
- Click row → navigate `/orders/{id}` (OrderDetail page)
- Click **+ New Order** → form

### `/orders/new` — Create Order
Form 3 bước, tất cả qua UI:
1. **Customer picker** — search bằng tên/email/city, dropdown debounced
2. **Product picker** — search SKU/name, filter category, click "+ Add" để add
3. **Cart review** — chỉnh quantity, xoá, xem total realtime
- Submit → backend FluentValidation → trả về order detail link
- Success page có 3 action: View Detail / Back / Create Another

### `/import` — Excel Import
Bulk import qua .xlsx file:
- **Tabs** cho 3 entity type: Customers / Products / Orders
- Mỗi tab: download template (.xlsx blank với headers), upload file, xem kết quả
- Result panel: 3 KPI (Total / Imported / Errors) + bảng errors chi tiết với row number
- Orders import gom theo `OrderRef` column → multi-line order

### `/stress` — Stress Test
Công cụ chứng minh OLTP write throughput + ETL pipeline:
- Input N orders + concurrency → bấm **Fire** → pool-based parallel requests
- ProgressBar + 4 KPIs realtime: Success / Failed / Elapsed / Throughput (ord/s)
- Buttons: **Trigger ETL** / **Force Compress** / **Reset Data**
- Activity log realtime

### Real-time qua SignalR (StrictMode-safe)
```typescript
// frontend/src/hooks/useEtlNotifications.ts
const conn = new HubConnectionBuilder()
    .withUrl('/hub/etl')
    .withAutomaticReconnect()
    .build();

conn.on('etl-completed', (evt) => {
    // Dashboard auto-refresh KPIs + charts
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

## 9d. 📥 Excel Import Pipeline

**Vấn đề:** Tạo data demo qua SSMS/REST cực kỳ phiền với non-technical user. Cần cho phép Business User chuẩn bị data trong Excel (file họ đã quen) rồi upload 1 phát.

**Giải pháp:** `ExcelImportService` dùng **ClosedXML** (Microsoft OpenXML wrapper) — parse `.xlsx` ở server, validate từng row, bulk insert.

### Hỗ trợ 3 entity type
| Entity | Columns | Quy tắc đặc biệt |
|---|---|---|
| **Customers** | FullName, Email, Phone, City | Email duplicate-check với DB hiện tại |
| **Products** | Sku, Name, Category, Brand, Price, StockQuantity | Sku duplicate-check, Price > 0 |
| **Orders** | OrderRef, CustomerEmail, Sku, Quantity | Multi-line: cùng OrderRef → gom 1 order, lookup Customer/Product bằng Email/SKU |

### Strategy: Validate Per-Row, Collect Errors
Không fail-fast. Đọc hết file, validate từng row, collect error vào list, vẫn save những row valid. Trả về `ImportResult`:
```json
{
  "totalRows": 100,
  "successCount": 95,
  "errorCount": 5,
  "errors": [
    { "row": 7, "message": "Duplicate email: a@example.com" },
    { "row": 23, "message": "Product not found by SKU: SKU-XYZ" }
  ]
}
```

### Template Download
Endpoint `GET /api/import/template/{kind}` trả về `.xlsx` blank với header + 1 row mẫu. Frontend dùng để user khỏi đoán tên column.

---

## 10. Cấu trúc thư mục

```
ECommerPipeline/
├── src/                                          ← Backend (.NET 9)
│   ├── ECommerPipeline.Domain/                   ← Entities, Enums (no deps)
│   │
│   ├── ECommerPipeline.Application/              ← Interfaces, DTOs, Validators
│   │   ├── Common/{Interfaces/, DTOs/PagedResult.cs}
│   │   ├── Orders/    {IOrderService, DTOs/, Validators/}
│   │   ├── Customers/ {ICustomerService, DTOs/}
│   │   ├── Products/  {IProductService, DTOs/}
│   │   ├── Reports/   {IReportService, DTOs/}
│   │   └── Import/    {IImportService, DTOs/}
│   │
│   ├── ECommerPipeline.Infrastructure/
│   │   ├── DependencyInjection.cs
│   │   ├── Initialization/{DatabaseInitializer, ResetService}.cs
│   │   ├── Persistence/
│   │   │   ├── Oltp/ {OltpDbContext, Configurations, Migrations}
│   │   │   └── Olap/ {OlapConnectionFactory, Scripts/OlapSchema.sql}
│   │   ├── Orders/    OrderService.cs            ← + paged list, detail
│   │   ├── Customers/ CustomerService.cs         ← search lookup
│   │   ├── Products/  ProductService.cs          ← search + categories
│   │   ├── Reports/   ReportService.cs           ← raw SQL với Dapper
│   │   ├── Import/    ExcelImportService.cs     ← ClosedXML parse + bulk insert
│   │   └── Etl/
│   │       ├── SalesEtlPipeline.cs               ← Extract→Transform→Load
│   │       ├── EtlJob.cs                         ← + Polly retry
│   │       └── CompressColumnstoreJob.cs         ← auto-compress 2AM
│   │
│   └── ECommerPipeline.Api/
│       ├── Program.cs                            ← minimal API + DI wiring
│       ├── Hubs/EtlNotificationHub.cs            ← SignalR hub
│       ├── Middleware/GlobalExceptionHandler.cs
│       ├── ECommerPipeline.Api.http              ← test flow đầy đủ
│       └── appsettings.json
│
├── frontend/                                     ← React SPA (Vite + TypeScript + Tremor)
│   ├── src/
│   │   ├── App.tsx                               ← React Router routes
│   │   ├── components/
│   │   │   ├── AppLayout.tsx                     ← Sidebar BI-tool layout
│   │   │   └── DateInput.tsx                     ← Native date input theo theme Tremor
│   │   ├── pages/
│   │   │   ├── Dashboard.tsx                     ← Tremor: Card/Metric/AreaChart/DonutChart/BarList
│   │   │   ├── OrdersList.tsx                    ← Paginated table + filter
│   │   │   ├── OrderDetail.tsx                   ← Line items + customer info
│   │   │   ├── CreateOrder.tsx                   ← 3-step form (customer/products/cart)
│   │   │   ├── ImportPage.tsx                    ← Excel upload với 3 tabs
│   │   │   └── StressTest.tsx                    ← Bulk-fire orders + admin
│   │   ├── hooks/useEtlNotifications.ts          ← SignalR client (StrictMode-safe)
│   │   ├── api/{client, reports, orders, lookups, imports}.ts ← Axios calls
│   │   ├── types/api.ts                          ← DTOs khớp backend
│   │   └── index.css                             ← @tailwind directives
│   ├── tailwind.config.js                        ← Tremor color/shadow tokens
│   ├── postcss.config.js
│   ├── package.json
│   └── vite.config.ts                            ← proxy /api + /hub to :5193
│
└── ECommerPipeline.sln
```

---

## 11. Screenshots

> 📸 **TODO:** Đính kèm các ảnh sau (sau khi bạn chụp), lưu vào `docs/screenshots/`:
>
> **Frontend (Tremor BI dashboard):**
> 1. `dashboard.png` — Dashboard với KPI cards + AreaChart + DonutChart + BarList
> 2. `orders-list.png` — Orders table với pagination + status filter
> 3. `create-order.png` — 3-step form (customer picker + product picker + cart)
> 4. `order-detail.png` — Order detail với line items + customer info + status badge
> 5. `import-excel.png` — Import page với tabs + upload + result summary
> 6. `stress-test.png` — Fire 1000 orders + Hangfire processing kế bên
>
> **Infra / debug:**
> 7. `hangfire.png` — Recurring Jobs (`sales-etl` + `compress-columnstore`)
> 8. `execution-plan.png` — SSMS Execution Plan: Columnstore Index Scan, Batch Mode

---

## 12. Hướng phát triển tiếp (v2 roadmap)

### Done ✅
- [x] SignalR — push báo cáo realtime lên dashboard
- [x] Frontend SPA — React Dashboard + Stress Test
- [x] Automated Columnstore Maintenance Job
- [x] Order Management (list / detail / create) — no SSMS needed
- [x] Excel Import (customers / products / orders) — ClosedXML + template download
- [x] BI-style UI refactor với Tremor + Tailwind CSS

### Đang muốn làm
- [ ] **CDC (Change Data Capture)** thay watermark — track DELETE/UPDATE chính xác (cần SQL Server Developer/Enterprise + sysadmin role)
- [ ] **Partitioning** theo tháng cho `fact.SalesOrderItem` — chuẩn bị khi lên 100M+ rows
- [ ] **MassTransit/RabbitMQ** — async event-driven thay vì polling 5 phút (near real-time ETL)
- [ ] **SCD Type 2** cho `dim.Customer`/`dim.Product` — giữ lịch sử thay đổi thay vì overwrite
- [ ] **Power BI / Grafana** đọc trực tiếp OLAP — share dashboard với non-technical user
- [ ] **xUnit + Testcontainers** — integration test với SQL Server container
- [ ] **Docker compose** — 1 lệnh up cả stack: SQL Server + Seq + API + Frontend
- [ ] **Authentication** — JWT + role-based authorization (admin xem được Stress Test, user thường chỉ Dashboard)
- [ ] **Excel Export** — download reports về .xlsx (đối ngẫu với Import)
