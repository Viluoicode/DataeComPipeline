# 📚 ECommerPipeline — Complete Study Guide

> Tài liệu học sâu cho **người đã build project nhưng muốn hiểu chắc** mỗi tech stack.
> Mỗi chapter ~400-600 dòng, có ví dụ từ chính project + Q&A phỏng vấn + self-test.
>
> **Cách dùng:** học 1 chapter / 1-2 ngày. Đọc → trace code thật trong project → trả lời Q&A → tự test.

---

## 📑 Mục lục

| # | Chapter | Độ ưu tiên | Thời gian học |
|---|---|---|---|
| 1 | [OLTP/OLAP + ETL Pipeline](#chapter-1) | ⭐⭐⭐⭐⭐ | 2 ngày |
| 2 | [Clean Architecture](#chapter-2) | ⭐⭐⭐⭐⭐ | 1 ngày |
| 3 | [JWT Authentication](#chapter-3) | ⭐⭐⭐⭐ | 1 ngày |
| 4 | [EF Core Deep Dive](#chapter-4) | ⭐⭐⭐⭐ | 2 ngày |
| 5 | [Async/Await + Cancellation](#chapter-5) | ⭐⭐⭐⭐ | 1 ngày |
| 6 | [SignalR Real-time](#chapter-6) | ⭐⭐⭐ | 1 ngày |
| 7 | [Hangfire Background Jobs](#chapter-7) | ⭐⭐⭐ | 1 ngày |
| 8 | [React Hooks + Context](#chapter-8) | ⭐⭐⭐ | 1 ngày |
| 9 | [Docker Compose](#chapter-9) | ⭐⭐⭐ | 1 ngày |
| 10 | [Tailwind + Tremor](#chapter-10) | ⭐⭐ | 0.5 ngày |

**Tổng: ~12 ngày** học có hệ thống.

---

<a name="chapter-1"></a>
# 📖 Chapter 1 — OLTP / OLAP + ETL Pipeline

## 1.1 Intuition — Tiệm phở đông khách

Tiệm phở 200 khách/ngày. Có **quầy order** và **văn phòng**:
- **Quầy order:** Ghi nhận khách, phải NHANH, format đơn giản
- **Văn phòng:** Tổng kết tháng, phân tích món bán chạy, cần CHÍNH XÁC

Nếu chỉ 1 cuốn sổ duy nhất:
- Khách order trong lúc bà chủ đang cộng → khách phải đợi
- Cộng đến nửa thì khách chen vào → cộng lại

**Giải pháp:** 2 cuốn sổ riêng + cuối ngày copy data từ Sổ A sang Sổ B.

→ **OLTP = Sổ quầy order. OLAP = Sổ văn phòng. ETL = copy data giữa 2 sổ.**

## 1.2 Concepts Core

### OLTP (Online Transaction Processing)

| Tiêu chí | Đặc điểm |
|---|---|
| Mục đích | Ghi nhanh (INSERT/UPDATE/DELETE) |
| Schema | Chuẩn hóa 3NF (Customers/Products/Orders tách bảng) |
| Index | B-tree mỏng, tối ưu seek theo PK |
| Concurrency | Hàng nghìn user cùng lúc |
| Query size | Đọc/ghi ~ 1-10 rows |
| Ví dụ | SQL Server row-store, PostgreSQL, MySQL |

### OLAP (Online Analytical Processing)

| Tiêu chí | Đặc điểm |
|---|---|
| Mục đích | Đọc nhanh (SELECT với JOIN/GROUP BY) |
| Schema | Star schema (Fact + Dimensions) |
| Index | Columnstore — lưu theo cột, nén ~10× |
| Concurrency | Vài user analyst |
| Query size | Aggregate triệu rows |
| Ví dụ | SQL Server Columnstore, BigQuery, Snowflake, Redshift |

### Star Schema — vẽ được trên giấy

```
              ┌──────────────┐
              │  dim.Date    │
              │ - DateKey    │  ← surrogate key int 20260519
              │ - Year       │
              │ - Month      │
              └──────┬───────┘
                     │
       ┌─────────────┼─────────────┐
       │             │             │
┌──────▼──────┐ ┌────▼────────────┐ ┌──▼─────────────┐
│ dim.Customer│ │ fact.Sales      │ │ dim.Product    │
│ -CustomerKey│←│ - DateKey       │→│ - ProductKey   │
│ - FullName  │ │ - CustomerKey   │ │ - Sku          │
│ - City      │ │ - ProductKey    │ │ - Category     │
└─────────────┘ │ - Quantity      │ └────────────────┘
                │ - LineTotal     │
                └─────────────────┘
                 ↑ FACT = đo lường
```

**Surrogate Key vs Natural Key:**
- `CustomerKey` (int identity) = surrogate, dùng trong fact table
- `CustomerId` (BIGINT) = natural, mapping ngược về OLTP

→ Lý do tách: nếu OLTP CustomerId thay đổi (vd merge 2 user), surrogate key OLAP vẫn nguyên.

### Columnstore Index — bí mật

**Row-store** lưu theo hàng:
```
Row 1: [id=1, name="iPhone", price=20M]
Row 2: [id=2, name="Mac", price=50M]
```

**Columnstore** lưu theo cột:
```
id:    [1, 2]
name:  ["iPhone", "Mac"]
price: [20M, 50M]
```

**Tại sao tốt cho analytics?**

Query `SUM(price) WHERE category='Phone'`:
- Row-store: đọc toàn bộ cell (id+name+price+cat) × N rows
- Columnstore: chỉ đọc 2 cột (price + cat) → 50% I/O

**Compression bonus:** cùng cột thường có data tương tự → nén tốt:
```
cat: ["Phone","Phone","Phone","Laptop","Laptop"] → "Phone×3, Laptop×2"
```

### ETL Pipeline — 3 bước

```
EXTRACT:   SELECT * FROM OrderItems WHERE Id > watermark
              ↓
TRANSFORM: Lookup keys, build DataTable
              ↓
LOAD:      SqlBulkCopy → fact.SalesOrderItem
           + UPDATE etl.Watermark
```

### Watermark Pattern

```sql
CREATE TABLE etl.Watermark (
    PipelineName       VARCHAR(100) PRIMARY KEY,
    LastProcessedRowId BIGINT       NOT NULL,
    LastProcessedAt    DATETIME2    NOT NULL
);
```

**Ưu điểm:**
- Idempotent (chạy lại không duplicate)
- Hiệu quả (chỉ extract delta)
- Resumable (fail giữa chừng → tiếp tục từ đúng chỗ)

**Nhược điểm:**
- Không bắt UPDATE/DELETE (chỉ INSERT)
- Đối thủ: **CDC (Change Data Capture)** — track mọi thay đổi

## 1.3 Code Walkthrough

### File 1: `OlapSchema.sql`
Path: `src/ECommerPipeline.Infrastructure/Persistence/Olap/Scripts/OlapSchema.sql`

```sql
CREATE TABLE fact.SalesOrderItem (
    SalesOrderItemKey BIGINT IDENTITY NOT NULL,
    DateKey           INT     NOT NULL,
    CustomerKey       INT     NOT NULL,
    ProductKey        INT     NOT NULL,
    OrderId           BIGINT  NOT NULL,
    OrderItemId       BIGINT  NOT NULL,
    Quantity          INT     NOT NULL,
    UnitPrice         DECIMAL(18,2) NOT NULL,
    LineTotal         DECIMAL(18,2) NOT NULL,
    EtlLoadedAt       DATETIME2     NOT NULL
);

-- ⭐ Đây là magic
CREATE CLUSTERED COLUMNSTORE INDEX CCI_SalesOrderItem
    ON fact.SalesOrderItem;
```

**Đọc hiểu:**
- Fact table có 9 cột — tất cả là số/key
- `CREATE CLUSTERED COLUMNSTORE INDEX` = chuyển bảng sang Columnstore
- Mỗi row được Hangfire ETL job insert vào (không có ai SELECT trực tiếp)

### File 2: `SalesEtlPipeline.cs`
Path: `src/ECommerPipeline.Infrastructure/Etl/SalesEtlPipeline.cs`

Đoạn quan trọng nhất:

```csharp
public async Task RunAsync(CancellationToken ct)
{
    using var conn = (SqlConnection)_olap.CreateConnection();
    await conn.OpenAsync(ct);

    // EXTRACT setup
    var watermark = await GetWatermarkAsync(conn, ct);
    await UpsertDimensionsAsync(conn, ct);
    var keyLookup = await LoadKeyLookupsAsync(conn, ct);

    while (!ct.IsCancellationRequested)
    {
        // Read delta from OLTP
        var rows = await _oltp.OrderItems
            .Where(i => i.Id > watermark)
            .OrderBy(i => i.Id)
            .Take(BatchSize)  // 5000
            .Select(i => new { i.Id, i.OrderId, ... })
            .ToListAsync(ct);
        
        if (rows.Count == 0) break;

        // TRANSFORM
        var fact = new DataTable();
        // ...build columns...
        foreach (var r in rows)
        {
            var dateKey = int.Parse(r.OrderDate.ToString("yyyyMMdd"));
            fact.Rows.Add(dateKey, custKey, prodKey, ...);
        }

        // LOAD
        using var tx = await conn.BeginTransactionAsync(ct);
        using var bulk = new SqlBulkCopy(conn, ..., tx);
        await bulk.WriteToServerAsync(fact, ct);
        
        await UpdateWatermarkAsync(conn, tx, maxId, ct);
        await tx.CommitAsync(ct);
        
        watermark = maxId;
    }
}
```

**Tại sao có `while` loop?** Vì ETL load batch 5000 rows. Nếu có 30k OrderItems mới → loop 6 lần. Lần 7 thấy `rows.Count == 0` → break.

**Tại sao `SqlBulkCopy` thay vì `INSERT`?**
- INSERT từng row = N network round-trips
- SqlBulkCopy = stream binary, ~100× nhanh hơn cho bulk load

### File 3: `ReportService.cs`
Path: `src/ECommerPipeline.Infrastructure/Reports/ReportService.cs`

```csharp
public Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(...)
{
    const string sql = @"
        SELECT  p.Category,
                CAST(COUNT(DISTINCT f.OrderId) AS BIGINT)    AS OrderCount,
                CAST(SUM(f.LineTotal) AS DECIMAL(18,2))      AS TotalRevenue
        FROM    fact.SalesOrderItem f         -- ← Columnstore table
        JOIN    dim.Product p   ON p.ProductKey = f.ProductKey
        JOIN    dim.Date    d   ON d.DateKey    = f.DateKey
        WHERE   d.[Date] BETWEEN @From AND @To
        GROUP BY p.Category
        ORDER BY TotalRevenue DESC;";

    return QueryAsync<SalesByCategoryRow>(sql, ...);
}
```

**Tại sao dùng Dapper thay vì EF Core?**
- EF Core: track entity, generate SQL từ LINQ → overhead cho read-only
- Dapper: raw SQL, không tracking, map kết quả vào DTO → nhanh hơn 2-3×

## 1.4 Benchmark thực tế (story phải thuộc)

| Scenario | OLAP (Columnstore) | OLTP (row-store) | Tỷ lệ |
|---|---|---|---|
| Delta store (data mới) | ~13,000 ms | ~80 ms | OLAP **chậm 160×** ⚠️ |
| Sau REORGANIZE COMPRESS | ~90 ms | ~1,200 ms | OLAP **nhanh 13×** ✅ |

**Insight quan trọng:** Columnstore không "free lunch". Data mới ở **delta store** (state=OPEN) chạy như row-store. Phải `ALTER INDEX REORGANIZE WITH COMPRESS_ALL_ROW_GROUPS` để chuyển sang COMPRESSED.

→ Project có `CompressColumnstoreJob` chạy 2AM hàng đêm tự động.

## 1.5 Interview Q&A

### Q1: "Vì sao em tách 2 database thay vì 1?"
> "OLTP cần B-tree index mỏng để INSERT nhanh nhưng kém với aggregate. OLAP cần Columnstore Index để GROUP BY/JOIN cực nhanh nhưng INSERT chậm. Một database không thể tốt cả 2. Em tách + đo benchmark thực: cùng query, OLAP nhanh hơn 13× trên 300k rows."

### Q2: "Columnstore Index làm việc thế nào?"
> "Lưu data theo COLUMN thay ROW. Query chỉ 2 cột → chỉ đọc 2 file. Compression cao vì cùng cột thường data tương tự, em đo được ~10× (50MB → 5MB)."

### Q3: "ETL của em chạy thế nào?"
> "3 bước. Extract: query OrderItems WHERE Id > watermark, chỉ lấy delta. Transform: lookup CustomerKey/ProductKey, tính DateKey. Load: SqlBulkCopy batch 5000 row/lần. Update watermark. Schedule mỗi 5 phút bằng Hangfire."

### Q4: "Watermark pattern là gì?"
> "Lưu Id cuối cùng đã xử lý vào etl.Watermark table. Lần sau chỉ extract WHERE Id > watermark. Ưu điểm: idempotent + hiệu quả + resumable. Nhược điểm: không bắt UPDATE/DELETE — cần CDC cho cái đó."

### Q5: "Vì sao SqlBulkCopy thay vì INSERT?"
> "INSERT từng row tốn N network round-trips. SqlBulkCopy stream binary protocol gửi batch lớn trong 1 round-trip. Nhanh hơn ~100× cho 5000 rows."

### Q6: "Vì sao Dapper cho OLAP, không EF Core?"
> "EF Core track entity, generate SQL từ LINQ — overhead cho analytics. Query phức tạp (GROUP BY, window) viết LINQ khó + sinh SQL không tối ưu. Dapper raw SQL trực tiếp, map kết quả vào DTO. Nhanh hơn 2-3× + đọc dễ."

### Q7: "Race condition giữa 2 ETL job xử lý thế nào?"
> "2 lớp. Lớp 1: Hangfire `[DisableConcurrentExecution(600)]`. Lớp 2: SQL `MERGE WITH (HOLDLOCK)` — atomic giữa match-check và insert."

### Q8: "Columnstore data mới sao chậm?"
> "Data mới ở 'delta store' — vẫn row-based B-tree tạm. Đủ 1,048,576 rows mới tự nén thành COMPRESSED. Volume thấp → ở mãi delta store → query chậm hơn cả OLTP. Em fix bằng job nightly REORGANIZE WITH COMPRESS."

### Q9: "Em sẽ thay watermark bằng gì để bắt DELETE/UPDATE?"
> "**CDC (Change Data Capture)**. SQL Server có built-in. Yêu cầu SQL Enterprise + sysadmin role. CDC tạo system tables log mọi INSERT/UPDATE/DELETE, ETL đọc từ đó. Em chưa dùng vì demo chỉ có Developer edition."

### Q10: "Demo project mà không có 1 triệu rows thì sao chứng minh Columnstore nhanh?"
> "Em đo trên 300k rows: OLAP 90ms vs OLTP 1200ms = 13× speedup. Còn nén từ 50MB → 5MB. Trên triệu rows chênh lệch sẽ to hơn nhiều (lý thuyết 100×). Em đã document khuyến nghị này trong README để recruiter benchmark thử."

## 1.6 Self-Test (làm trong 1 buổi)

### Bài 1 — Vẽ trên giấy
1. Vẽ kiến trúc OLTP/OLAP/ETL của project
2. Vẽ star schema (1 fact + 3 dims)
3. Mô tả 3 bước ETL bằng pseudocode

### Bài 2 — SSMS benchmark
1. Connect SQL Server local hoặc Docker SQL
2. `USE ECommerPipeline_Olap; SET STATISTICS TIME ON;`
3. Chạy query group by category (trên fact.SalesOrderItem)
4. Chạy query tương tự trên `ECommerPipeline_Oltp` (OrderItems JOIN Orders JOIN Products)
5. So sánh `elapsed time` lần 3

### Bài 3 — Code trace
Mở `SalesEtlPipeline.cs`, đọc từng dòng `RunAsync()`. Trả lời:
- Vì sao có `using var conn = ...`?
- Tại sao `await UpsertDimensionsAsync` chạy BEFORE main loop?
- Nếu xóa `await tx.CommitAsync(ct)` thì sao?

## 1.7 Common Pitfalls

❌ "Em chọn Columnstore vì nhanh"
✅ "Em chọn Columnstore vì optimal cho analytical query — đặc biệt aggregate trên triệu rows. Trên dataset nhỏ chưa phát huy, cần compress + warm cache."

❌ "ETL chạy mỗi 5 phút" (không giải thích)
✅ "ETL chạy mỗi 5 phút balance giữa freshness (data mới hiện trên dashboard nhanh) và DB load (không spam SQL Server). Production có thể giảm xuống 1 phút hoặc dùng CDC real-time."

---

<a name="chapter-2"></a>
# 📖 Chapter 2 — Clean Architecture

## 2.1 Intuition — Lớp học bánh kem

Tiệm bánh kem có 4 phòng tách biệt:
- **Phòng kho** (Domain): nguyên liệu — bột, đường, kem
- **Phòng công thức** (Application): recipe — "Cách làm bánh sinh nhật"
- **Phòng bếp** (Infrastructure): công cụ — lò nướng, máy đánh trứng
- **Quầy thu ngân** (API): tiếp khách — nhận order, giao bánh

**Quy tắc vàng:** Phòng trong KHÔNG biết gì về phòng ngoài.
- Phòng công thức KHÔNG biết "lò nướng của hãng nào"
- Đổi lò nướng (hỏng → mua mới) → phòng công thức không cần biết
- Đổi cách giao bánh (giao tận nhà → khách đến lấy) → phòng kho không quan tâm

→ **Đây là Dependency Inversion**: phòng ngoài phục vụ phòng trong, không ngược lại.

## 2.2 4 Layers trong project

```
┌────────────────────────────────────────────┐
│ ECommerPipeline.Api          ← HTTP entry  │ (outer)
└────────────────────────────────────────────┘
              ↓ uses
┌────────────────────────────────────────────┐
│ ECommerPipeline.Infrastructure ← Tech impl │
│   ├─ EF Core (SQL Server)                   │
│   ├─ Dapper (OLAP)                          │
│   ├─ Hangfire, SignalR, BCrypt              │
│   └─ External services                      │
└────────────────────────────────────────────┘
              ↓ uses
┌────────────────────────────────────────────┐
│ ECommerPipeline.Application  ← Interfaces  │
│   ├─ IOrderService, IAuthService, etc.     │
│   ├─ DTOs (CreateOrderRequest, ...)        │
│   ├─ Validators (FluentValidation)         │
│   └─ NO tech implementation                 │
└────────────────────────────────────────────┘
              ↓ uses
┌────────────────────────────────────────────┐
│ ECommerPipeline.Domain       ← Entities    │ (inner)
│   ├─ Customer, Order, Product, OrderItem   │
│   ├─ Enums (OrderStatus, UserRole)         │
│   ├─ NO dependencies on anything            │
│   └─ Pure business model                    │
└────────────────────────────────────────────┘
```

### Dependency Flow (quan trọng)

```
Api      ──────► Application + Infrastructure
              │
Infrastructure ──► Application
              │
Application  ──────► Domain
              │
Domain       ──────► (nothing)  ← purity
```

**Mũi tên chỉ chiều "tham chiếu"** (project A reference project B).

Tại sao quan trọng?
- Domain không biết EF Core → đổi sang Postgres chỉ sửa Infrastructure
- Application không biết Hangfire → đổi sang Quartz cũng OK
- Test dễ vì mock interface

## 2.3 Code Walkthrough

### File 1: Domain entity (zero dependencies)

`src/ECommerPipeline.Domain/Entities/Customer.cs`:

```csharp
using ECommerPipeline.Domain.Common;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Domain.Entities;

public class Customer : BaseEntity
{
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    // ...
}
```

**Để ý:** Chỉ `using` Domain.Common và Domain.Enums (cùng dự án). KHÔNG `using` EF Core, AspNetCore, anything external.

### File 2: Application interface

`src/ECommerPipeline.Application/Orders/IOrderService.cs`:

```csharp
public interface IOrderService
{
    Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderQueryParams query, CancellationToken ct = default);
    Task<OrderDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);
}
```

**Để ý:** Chỉ định nghĩa "WHAT" (chữ ký method), không "HOW" (logic). Application không biết implementation.

### File 3: Infrastructure implementation

`src/ECommerPipeline.Infrastructure/Orders/OrderService.cs`:

```csharp
public class OrderService : IOrderService  // ← implement Application interface
{
    private readonly OltpDbContext _db;     // ← biết EF Core (tech)
    
    public async Task<OrderCreatedResponse> CreateAsync(...)
    {
        // Logic dùng EF Core ở đây
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        // ...
    }
}
```

**Để ý:** "HOW" được implement ở Infrastructure. Đổi từ EF Core sang Dapper chỉ thay file này, không ai khác biết.

### File 4: DI wiring

`src/ECommerPipeline.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IOrderService, OrderService>();
//      ↑                ↑
//      interface         implementation
//      (Application)     (Infrastructure)
```

→ Khi controller cần `IOrderService`, .NET DI tự inject `OrderService`. Controller không biết tên implementation.

### File 5: API endpoint

`src/ECommerPipeline.Api/Program.cs`:

```csharp
app.MapPost("/api/orders", async (
    CreateOrderRequest req,
    IOrderService svc,        // ← inject interface, không phải OrderService
    CancellationToken ct) =>
{
    return Results.Ok(await svc.CreateAsync(req, ct));
}).WithTags("Orders");
```

**Để ý:** API gọi `svc.CreateAsync(...)` — không quan tâm impl là gì.

## 2.4 Tại sao Clean Arch trong project?

### Case 1: Đổi database

Hiện tại dùng SQL Server. Sếp bảo "đổi sang PostgreSQL":
- **Không Clean Arch:** sửa khắp nơi, Controller cũng đụng EF Core
- **Có Clean Arch:** chỉ sửa Infrastructure/Persistence/Oltp. Domain + Application không đổi 1 dòng.

### Case 2: Đổi background job library

Đang dùng Hangfire. Muốn đổi sang Quartz:
- Sửa Infrastructure/Etl + DependencyInjection.cs
- Application + Domain không đổi gì

### Case 3: Test unit

Test OrderService:
```csharp
// Mock interface, không cần DB thật
var mockDb = new Mock<IOltpDbContext>();
var service = new OrderService(mockDb.Object);
var result = await service.CreateAsync(...);
```

→ Nếu OrderService dùng `DbContext` cụ thể (không có interface) → khó mock → khó test.

## 2.5 Variant: Jason Taylor's pragmatic Clean Arch

Project bạn KHÔNG strict 100% Clean Arch. Cụ thể:
- `IOltpDbContext` trong Application **reference EF Core** (`DbSet<T>`)
- Strict Clean Arch sẽ tách ra `IRepository<T>` không có EF Core

**Tại sao chấp nhận?**
> "Pragmatic Clean Architecture của Jason Taylor — chấp nhận Application phụ thuộc EF Core abstractions (DbContext, DbSet) vì:
> 1. EF Core đã abstract đủ tốt
> 2. Repository pattern duplicate EF Core
> 3. Realistic — 95% production .NET project dùng EF Core
>
> Em chấp nhận trade-off này. Nếu cần đổi ORM (rất hiếm), em sẽ refactor."

→ Recruiter senior sẽ ấn tượng vì biết trade-off, không cứng nhắc.

## 2.6 Interview Q&A

### Q1: "Em chọn Clean Architecture, tại sao?"
> "4 layer tách biệt: Domain (entities thuần), Application (interfaces + DTOs), Infrastructure (tech impl), Api (HTTP). Dependency flow chỉ chiều ngoài → trong. Lợi ích: dễ test (mock interface), dễ thay tech (đổi DB chỉ sửa Infrastructure), code rõ trách nhiệm."

### Q2: "Vì sao Domain không có dependency?"
> "Để tách business logic khỏi tech. Domain biểu diễn business rule (Customer có Email, Order có Status). Không phụ thuộc EF Core, AspNetCore, ... Khi đổi tech, Domain vẫn nguyên. Domain testable thuần mà không cần DB."

### Q3: "Dependency Inversion Principle là gì?"
> "High-level modules không phụ thuộc low-level. Cả 2 phụ thuộc abstraction. Trong project em: OrderService (Application) không phụ thuộc EF Core (low-level). Nó phụ thuộc OltpDbContext qua interface. DI container inject implementation lúc runtime."

### Q4: "Vì sao Application chứa IOltpDbContext có DbSet (kiểu EF Core)?"
> "Đây là pragmatic Clean Arch của Jason Taylor. Strict sẽ tách Repository pattern, nhưng duplicate EF Core mà không thêm value. Em chấp nhận Application reference EF Core abstractions vì DbContext/DbSet đã đủ abstract. Nếu cần đổi ORM (rất hiếm), em sẽ refactor."

### Q5: "Test Service em viết thế nào?"
> "Mock IOltpDbContext bằng Moq. Setup behavior cho DbSet (vd: `_mockDb.Setup(x => x.Customers).Returns(fakeQueryable)`). Inject mock vào constructor service. Assert result. Cần EF Core in-memory hoặc Testcontainers cho integration test thực."

## 2.7 Self-Test

### Bài 1 — Vẽ dependency graph
Mở solution explorer, vẽ mũi tên giữa 4 project (tham chiếu chiều nào).

### Bài 2 — Tìm violation
Mở Domain/Entities/Customer.cs. Thêm thử `using Microsoft.EntityFrameworkCore;` → build → quan sát.

→ Build vẫn pass (vì Domain.csproj không reference EF Core). Nhưng đây là **anti-pattern** — Domain đang biết về tech.

### Bài 3 — Trace flow
"Khi user POST /api/orders, đi qua những file nào?"

Answer:
1. `Program.cs` — route handler nhận request
2. Validator (FluentValidation) — check input
3. `OrderService.CreateAsync` — implement logic
4. `OltpDbContext.Orders.Add` — EF Core track entity
5. `SaveChangesAsync` — sinh SQL INSERT
6. SQL Server lưu vào Orders table

## 2.8 Common Pitfalls

❌ "Em copy Clean Arch từ internet"
✅ "Em chọn 4-layer của Jason Taylor để cân bằng giữa strict Clean Arch và pragmatic. Application reference EF Core abstractions, không qua Repository."

❌ "Clean Arch chỉ cho enterprise"
✅ "Clean Arch dùng được cho project mọi size. Lợi ích test/maintain. Overhead chỉ là chia thêm vài csproj."

---

<a name="chapter-3"></a>
# 📖 Chapter 3 — JWT Authentication

## 3.1 Intuition — Sticker đại lý vé concert

Đại lý vé concert phát **sticker dán cổ tay**:
- Sticker có **tên show + ngày + chỗ ngồi** (claims)
- Sticker có chữ ký phát quang **không giả được** (HMAC signature)
- Bảo vệ ở cửa nhìn sticker → biết bạn được vào → KHÔNG cần check lại với phòng vé

→ **JWT = sticker.** Server cấp token, client gửi token mỗi request, server verify token mà không cần query DB.

**So với session truyền thống:**
- Session: server lưu state (Redis/DB) cho mỗi user. Mỗi request server query để check session valid không.
- JWT: stateless. Server không lưu gì. Token tự chứng minh đủ tin cậy nếu signature đúng.

## 3.2 JWT Anatomy

JWT 1 chuỗi 3 phần ngăn bằng `.`:
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9 . eyJzdWIiOiIxMjMifQ . SflKxwRJSMeKKF2QT4f...
↑ Header (Base64)                       ↑ Payload (Base64)    ↑ Signature
```

### Header
```json
{
  "alg": "HS256",   // algorithm
  "typ": "JWT"
}
```

### Payload (claims)
```json
{
  "sub": "42",                   // subject = user id
  "email": "admin@ecom.com",
  "role": "Admin",
  "exp": 1716000000,             // expiry timestamp
  "iss": "ECommerPipeline",      // issuer
  "aud": "ECommerPipeline.Client",
  "jti": "abc-xyz-..."           // unique id
}
```

### Signature
```
HMACSHA256(
   base64(header) + "." + base64(payload),
   secret_key
)
```

→ **Verify token:** decode header+payload, recompute signature, compare. Nếu match → token valid + chưa bị sửa.

**Quan trọng:** JWT KHÔNG mã hóa payload — chỉ ký. Đừng bỏ password vào payload!

## 3.3 Code Walkthrough

### File 1: JwtTokenService

`src/ECommerPipeline.Infrastructure/Auth/JwtTokenService.cs`:

```csharp
public (string token, DateTime expiresAt) CreateAccessToken(Customer user)
{
    var now = DateTime.UtcNow;
    var expires = now.AddMinutes(_opt.AccessTokenMinutes);  // 60 minutes

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Role, user.Role.ToString()),
    };

    var token = new JwtSecurityToken(
        issuer: _opt.Issuer,
        audience: _opt.Audience,
        claims: claims,
        notBefore: now,
        expires: expires,
        signingCredentials: _credentials);  // ← HMAC-SHA256 với secret

    return (new JwtSecurityTokenHandler().WriteToken(token), expires);
}

public string CreateRefreshToken()
{
    var bytes = RandomNumberGenerator.GetBytes(64);   // ← cryptographically random
    return Convert.ToBase64String(bytes);
}
```

**Note:**
- Access token = JWT signed, 60 phút
- Refresh token = opaque random string 512 bits, 7 ngày, lưu DB

### File 2: AuthService.Login

```csharp
public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct)
{
    var customer = await _db.Customers
        .FirstOrDefaultAsync(c => c.Email == emailNorm, ct);

    if (customer is null
        || string.IsNullOrEmpty(customer.PasswordHash)
        || !BC.Verify(req.Password, customer.PasswordHash))
    {
        throw new UnauthorizedAccessException("Invalid email or password.");
        // ← Identical message: don't leak whether email exists
    }

    customer.LastLoginAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    return await IssueTokensAsync(customer, ct);
}
```

**Bảo mật quan trọng:**
- `BC.Verify` = BCrypt verify (slow by design, chống brute-force)
- Message lỗi giống nhau dù email không tồn tại hay password sai → không leak info

### File 3: JWT Middleware setup

`src/ECommerPipeline.Api/Program.cs`:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.FromMinutes(1),  // ← grace period for clock drift
        };
    });
```

### File 4: Frontend axios interceptor

`frontend/src/api/client.ts`:

```typescript
// Request: inject Bearer header
api.interceptors.request.use((config) => {
    const session = getStoredAuth()
    if (session?.accessToken) {
        config.headers.Authorization = `Bearer ${session.accessToken}`
    }
    return config
})

// Response: auto refresh on 401
api.interceptors.response.use(
    r => r,
    async (error: AxiosError) => {
        if (error.response?.status === 401 && !original._retry) {
            original._retry = true
            const newToken = await tryRefresh()  // ← call /api/auth/refresh
            if (newToken) {
                original.headers.Authorization = `Bearer ${newToken}`
                return api.request(original)  // ← retry original
            }
        }
        return Promise.reject(error)
    }
)
```

## 3.4 Auth flow chi tiết

### Login flow
```
1. User nhập email + password trong form
   ↓
2. POST /api/auth/login { email, password }
   ↓
3. Backend: BCrypt.Verify password vs PasswordHash trong DB
   ↓
4. Match → Issue access token (60m JWT) + refresh token (7d random)
   ↓
5. Save refresh token vào RefreshTokens table
   ↓
6. Return { accessToken, refreshToken, expiresAt, user }
   ↓
7. Frontend: lưu vào localStorage
```

### Protected request flow
```
1. User click "My Orders"
   ↓
2. GET /api/orders với header "Authorization: Bearer eyJ..."
   ↓
3. JwtBearer middleware decode token
   - Verify signature (HMAC-SHA256 với secret)
   - Verify exp chưa hết
   - Verify iss + aud
   ↓
4. Nếu valid → set HttpContext.User với claims (sub, role, ...)
   ↓
5. Endpoint check [RequireAuthorization] → cho qua
   ↓
6. Return orders của user theo sub claim
```

### Refresh flow (token expired)
```
1. Access token hết hạn sau 60 phút
   ↓
2. Bất kỳ API call → backend trả 401
   ↓
3. Axios interceptor catch 401
   ↓
4. POST /api/auth/refresh { refreshToken }
   ↓
5. Backend lookup RefreshTokens table
   - Token chưa revoke + chưa expire → OK
   - Rotate: revoke old + issue new pair
   ↓
6. Frontend lưu token mới, retry original request
   ↓
7. User không thấy gì khác lạ
```

## 3.5 Bảo mật chi tiết

### BCrypt vs SHA256

❌ SHA256(password): rất nhanh → brute-force GPU 1 tỷ guess/giây
✅ BCrypt(password, workFactor=11): chậm ~250ms → brute-force gần như không thể

### Refresh token rotation

Token cũ dùng 1 lần → tự revoke → issue token mới. Nếu attacker steal token cũ:
- User đã dùng → token đã rotate → attacker token không hoạt động
- Attacker dùng trước → user dùng sau → user nhận lỗi → biết bị stolen

### Secret 256 bits+

```csharp
var keyBytes = Encoding.UTF8.GetBytes(_opt.Secret);
if (keyBytes.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 chars");
```

→ Secret < 256 bits → HMAC-SHA256 không an toàn.

### ClockSkew

```csharp
ClockSkew = TimeSpan.FromMinutes(1)
```

→ Khi server time và client time lệch nhẹ (1-2 giây), token sắp hết hạn vẫn được accept thêm 1 phút. Mặc định là 5 phút, em set 1 phút cho strict hơn.

## 3.6 Interview Q&A

### Q1: "JWT khác Session ra sao?"
> "Session lưu state server-side (Redis/DB), mỗi request server query check. JWT stateless — token tự chứng minh nếu signature đúng. JWT scale tốt hơn cho microservices (không cần shared session store). Đổi lại: JWT không thể invalidate trước expiry (trừ khi có blacklist hoặc refresh token rotation)."

### Q2: "Tại sao có cả access + refresh token?"
> "Access token short-lived (60m) để giảm risk nếu bị stolen. Refresh token long-lived (7d) lưu DB để revoke được — JWT không tự revoke được. Refresh exchange refresh token lấy access token mới, user không phải re-login."

### Q3: "BCrypt vs SHA256 với salt?"
> "SHA256 rất nhanh (~ns/hash), GPU brute-force tỷ guess/s. BCrypt slow by design (workFactor 11 = ~250ms/hash). Salt built-in. Attacker GPU brute-force gần như không khả thi. Em chọn BCrypt vì industry standard cho password hashing."

### Q4: "JWT payload có thể thấy bằng base64decode → unsafe không?"
> "JWT không mã hóa — chỉ ký. Payload public, ai cũng đọc được. Nhưng SỬA payload → signature mismatch → server reject. Vì vậy không bỏ data nhạy cảm (password) vào payload. Em chỉ bỏ Id, Email, Role."

### Q5: "Em refresh token thế nào?"
> "Frontend axios interceptor: nếu response 401 + chưa retry → gọi /api/auth/refresh với refresh token → backend rotate (revoke old, issue new pair) → frontend lưu pair mới, retry original request. Coalesce nhiều 401 đồng thời vào 1 refresh call."

### Q6: "Bị stolen access token thì sao?"
> "JWT không revoke được trực tiếp. 3 lớp bảo vệ: (1) access token expire 60m, (2) refresh token rotation phát hiện reuse, (3) trong production sẽ có blacklist trên Redis cho critical operations. Em không implement blacklist trong demo vì overkill."

### Q7: "Role-based authorization thế nào?"
> "Token có claim 'role' (Customer/Staff/Admin). Endpoint dùng `[Authorize(Roles=\"Admin,Staff\")]` hoặc `.RequireAuthorization(p => p.RequireRole(...))`. Middleware tự check claim trong HttpContext.User."

### Q8: "Vì sao SignalR cần query string token thay vì header?"
> "WebSocket upgrade request không có cách custom Authorization header trên browser. Frontend phải truyền qua query `?access_token=...`. Server JwtBearer.Events.OnMessageReceived catch nó và set thành ctx.Token. Em đã wire trong Program.cs."

## 3.7 Self-Test

### Bài 1 — Decode JWT bằng tay
1. Login admin@ecom.com → DevTools → Local Storage → copy accessToken
2. Lên https://jwt.io paste vào
3. Đọc payload — xác nhận claims đúng (sub=id admin, role=Admin)

### Bài 2 — Test expiry
1. Sửa `appsettings.json`: `AccessTokenMinutes: 1`
2. Restart app, login
3. Đợi 1.5 phút → bấm refresh dashboard
4. Mở Network tab — quan sát:
   - Request /api/reports/* → 401
   - Auto fired POST /api/auth/refresh → 200
   - Retry original /api/reports/* → 200
5. → Hiểu refresh flow

### Bài 3 — Test stolen token
1. Login, copy accessToken
2. Open Incognito → DevTools → Application → Local Storage → set ecom.auth = { accessToken: "stolen-token", ... }
3. Reload → vào được /my-orders mà không cần login
4. → Hiểu vì sao JWT cần HTTPS + secure storage

## 3.8 Common Pitfalls

❌ "JWT là encryption" → Sai. JWT là signed, không encrypted.
✅ "JWT signed — verify tampering. Để encrypt cần JWE (JSON Web Encryption)."

❌ Lưu JWT trong localStorage → XSS risk
✅ "Em lưu trong localStorage cho demo. Production nên dùng HttpOnly cookie + CSRF token để tránh XSS đánh cắp."

---

<a name="chapter-4"></a>
# 📖 Chapter 4 — EF Core Deep Dive

## 4.1 Intuition — Trợ lý phiên dịch

Bạn là CEO Việt Nam, nói chuyện với khách Nhật. Trợ lý phiên dịch:
- Nghe bạn nói tiếng Việt (LINQ)
- Dịch sang tiếng Nhật (SQL)
- Gửi cho khách (Database)
- Nghe trả lời tiếng Nhật (SQL result)
- Dịch ngược tiếng Việt (C# objects)

→ EF Core = trợ lý phiên dịch giữa C# (LINQ) và SQL Server.

**Vì sao dùng:**
- Không phải viết SQL tay (tránh SQL injection)
- Migration tự động (schema versioning)
- Track entities (auto detect changes)
- Type-safe (compile-time check)

**Đánh đổi:**
- Slow hơn raw SQL (~10-30% overhead)
- Phức tạp với complex queries
- Lazy loading có thể gây N+1 problem

## 4.2 Core Concepts

### DbContext

```csharp
public class OltpDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    // ...
}
```

→ Lớp đại diện cho session với DB. **1 DbContext = 1 unit of work**.

### Change Tracking

EF tự track mọi thay đổi entity được Add/Update:
```csharp
var customer = await _db.Customers.FirstAsync(c => c.Id == 42);
customer.Email = "new@email.com";  // EF biết đã thay đổi
await _db.SaveChangesAsync();      // sinh UPDATE SQL
```

### Migration

```bash
dotnet ef migrations add AddAuth  # tạo migration
dotnet ef database update          # apply lên DB
```

Project có sẵn migrations:
- `20260518061228_InitialOltp.cs` — tạo Customer/Order/Product/OrderItem
- `20260522062334_AddAuth.cs` — thêm PasswordHash, RefreshTokens

### LINQ → SQL

```csharp
var orders = await _db.Orders
    .Where(o => o.OrderDate >= from)
    .OrderByDescending(o => o.OrderDate)
    .Skip(20)
    .Take(20)
    .ToListAsync();
```

EF sinh SQL:
```sql
SELECT TOP 20 *
FROM Orders
WHERE OrderDate >= @from
ORDER BY OrderDate DESC
OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY;
```

## 4.3 Code Walkthrough

### File 1: DbContext

`src/ECommerPipeline.Infrastructure/Persistence/Oltp/OltpDbContext.cs`:

```csharp
public class OltpDbContext : DbContext, IOltpDbContext
{
    public OltpDbContext(DbContextOptions<OltpDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(OltpDbContext).Assembly);
        base.OnModelCreating(b);
    }
}
```

→ `ApplyConfigurationsFromAssembly` tự tìm các class `IEntityTypeConfiguration<T>` trong assembly.

### File 2: Entity Configuration

`src/ECommerPipeline.Infrastructure/Persistence/Oltp/Configurations/OrderConfiguration.cs`:

```csharp
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.ToTable("Orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        e.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.Status).HasConversion<int>();  // ← enum lưu thành int

        e.HasIndex(x => x.OrderNumber).IsUnique();
        e.HasIndex(x => x.OrderDate);
        e.HasIndex(x => new { x.CustomerId, x.OrderDate });  // ← composite index

        e.HasOne(x => x.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasMany(x => x.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Để ý:**
- `HasIndex(...).IsUnique()` — DB index
- `HasConversion<int>()` — enum → int trong DB
- `DeleteBehavior.Restrict` — không cho xóa Customer nếu có Order
- `DeleteBehavior.Cascade` — xóa Order → xóa hết OrderItems

### File 3: Query với Include + Select

`OrderService.GetByIdAsync`:

```csharp
return await _db.Orders.AsNoTracking()
    .Where(o => o.Id == id)
    .Select(o => new OrderDetailDto(
        o.Id,
        o.OrderNumber,
        o.CustomerId,
        o.Customer.FullName,        // ← auto JOIN Customers
        o.Customer.Email,
        o.OrderDate,
        o.Status,
        o.TotalAmount,
        o.Items.Select(i => new OrderItemDetailDto(    // ← auto JOIN OrderItems + Products
            i.ProductId,
            i.Product.Sku,
            i.Product.Name,
            i.Quantity,
            i.UnitPrice,
            i.LineTotal
        )).ToList()))
    .FirstOrDefaultAsync(ct);
```

→ EF Core sinh SQL với JOIN cần thiết:
```sql
SELECT TOP 1 o.Id, o.OrderNumber, c.FullName, c.Email, ...
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
LEFT JOIN OrderItems i ON i.OrderId = o.Id
LEFT JOIN Products p ON p.Id = i.ProductId
WHERE o.Id = @id
```

### File 4: AsNoTracking

```csharp
_db.Customers.AsNoTracking().Where(...).ToListAsync()
```

→ Đọc-only, không cần track. EF Core skip change tracking → nhanh hơn 30%. Dùng khi không cần update.

### File 5: SaveChangesAsync transaction

```csharp
_db.Orders.Add(order);  // chỉ track, không touching DB
await _db.SaveChangesAsync(ct);  // ← sinh SQL transaction
```

EF Core tự wrap trong transaction. Nếu fail giữa chừng → rollback tất cả.

## 4.4 N+1 Problem

```csharp
// ❌ BAD — N+1 queries
var orders = await _db.Orders.ToListAsync();
foreach (var o in orders)
{
    Console.WriteLine(o.Customer.FullName);  // ← lazy load → 1 query mỗi order
}
// Total: 1 + N queries (N = số order)

// ✅ GOOD — 1 query
var orders = await _db.Orders.Include(o => o.Customer).ToListAsync();
foreach (var o in orders)
{
    Console.WriteLine(o.Customer.FullName);  // ← đã eager load
}
// Total: 1 query với JOIN
```

→ Trong project em **không có lazy loading** — phải `.Include()` hoặc `.Select(o => new { o.Customer.FullName })` explicit. An toàn hơn.

## 4.5 Migration workflow

```bash
# 1. Sửa entity (vd thêm field)
# 2. Tạo migration
dotnet ef migrations add AddPhoneVerified \
    --project src/ECommerPipeline.Infrastructure \
    --startup-project src/ECommerPipeline.Api \
    --context OltpDbContext

# 3. Review file migration sinh ra
# 4. Apply lên DB
dotnet ef database update

# Hoặc app tự apply lúc startup
await _db.Database.MigrateAsync(ct);  // trong DatabaseInitializer
```

## 4.6 Interview Q&A

### Q1: "EF Core khác Dapper thế nào?"
> "EF Core là full ORM: track entity, sinh SQL từ LINQ, migration, navigation properties. Dapper là micro-ORM: chỉ map SQL result vào object. EF Core dễ dùng, Dapper nhanh hơn 2-3× cho read-only. Em dùng EF cho OLTP (write+update), Dapper cho OLAP (read analytical)."

### Q2: "AsNoTracking dùng khi nào?"
> "Khi query read-only, không cần update. EF skip change tracking → nhanh hơn ~30%. Dùng cho list page, search, analytics. Không dùng nếu sẽ modify entity và save."

### Q3: "N+1 problem là gì?"
> "Query 1 list (1 query) rồi access navigation property của từng item (N queries thêm). Fix bằng .Include() hoặc .Select projection. Project em không có lazy loading nên dev phải explicit, ít bị N+1."

### Q4: "Migration vs Database First?"
> "Code First (migration): viết entity → tạo migration → apply DB. Database First: có DB sẵn → scaffold entity. Em chọn Code First vì dev workflow tốt hơn — schema theo code, version control với git."

### Q5: "Concurrency token là gì?"
> "Field đặc biệt (vd RowVersion timestamp) track concurrent update. 2 user load cùng row, cả 2 sửa, 1 save trước → người thứ 2 save → EF check token mismatch → throw DbUpdateConcurrencyException. Tránh lost update. Project em chưa implement vì demo đơn giản."

### Q6: "EF Core sinh SQL có tối ưu không?"
> "90% trường hợp OK với simple query. Complex query (CTE, window function, hint) thì EF sinh SQL không optimal. Khi đó em fallback Dapper raw SQL. Cách check: `query.ToQueryString()` xem SQL EF sinh ra."

### Q7: "Em handle transaction thế nào?"
> "Default SaveChangesAsync tự wrap transaction. Cần multiple SaveChanges trong 1 transaction → dùng `_db.Database.BeginTransactionAsync()`. Em không cần explicit trong project vì mỗi business operation 1 SaveChanges duy nhất."

## 4.7 Self-Test

### Bài 1 — Quan sát SQL EF sinh ra
```csharp
var query = _db.Orders
    .Include(o => o.Customer)
    .Where(o => o.OrderDate >= DateTime.Today)
    .OrderByDescending(o => o.OrderDate);

Console.WriteLine(query.ToQueryString());
```

→ In ra console SQL EF sẽ chạy.

### Bài 2 — Đo AsNoTracking
- Chạy query 1000 lần có tracking
- Chạy query 1000 lần với AsNoTracking
- So thời gian

### Bài 3 — Tạo migration mới
1. Thêm field `Customer.AvatarUrl?`
2. `dotnet ef migrations add AddCustomerAvatar`
3. Đọc file migration sinh ra
4. `dotnet ef database update` apply
5. Verify trong SSMS

## 4.8 Common Pitfalls

❌ Load full entity rồi access 1 field
✅ `.Select(o => o.FullName)` — chỉ load field cần

❌ Loop call await trong SaveChanges
✅ AddRange + 1 lần SaveChanges

❌ Track entity không cần
✅ AsNoTracking() khi read-only

---

<a name="chapter-5"></a>
# 📖 Chapter 5 — Async/Await + Cancellation

## 5.1 Intuition — Đầu bếp đa nhiệm

**Sync (đồng bộ):** Đầu bếp luộc nước → đợi 10 phút nước sôi → cắt rau → đợi 5 phút → nấu phở. Tổng: 25 phút.

**Async (bất đồng bộ):** Đầu bếp bật nồi nước → trong khi đợi → cắt rau. Khi nước sôi → nấu. Tổng: 15 phút.

→ Async giúp **không block thread** khi đợi I/O (DB, network, file).

## 5.2 Core Concepts

### Task<T>

```csharp
Task<int> task = SomeAsyncMethod();  // bắt đầu chạy, nhưng chưa await
int result = await task;              // chờ kết quả
```

`Task<T>` = "promise" trả về `T` trong tương lai.

### await

`await` không block thread. Nó **đăng ký callback** "khi task xong, tiếp tục từ đây".

```csharp
public async Task<Order> CreateOrderAsync(...)
{
    // ... setup
    var products = await _db.Products.ToListAsync();  // ← thread "tạm trả" về pool
    // .... thread quay lại đây khi DB trả kết quả
    var order = new Order { ... };
    await _db.SaveChangesAsync();  // ← lại trả thread
    return order;
}
```

→ Trong khi DB chạy query, thread không sit idle — nó phục vụ request khác.

### Sync vs Async benchmark

Web server 100 request đồng thời, mỗi query DB 100ms:
- **Sync:** Cần 100 thread (mỗi thread đợi DB) → tốn RAM, slow
- **Async:** Cần ~5-10 thread → cùng phục vụ 100 request

→ Async **scale tốt hơn** cho I/O-bound workload.

## 5.3 Code Walkthrough

### File 1: Async endpoint

`Program.cs`:
```csharp
app.MapPost("/api/orders", async (
    CreateOrderRequest req,
    IOrderService svc,
    CancellationToken ct) =>
{
    return Results.Ok(await svc.CreateAsync(req, ct));
})
```

**Để ý:**
- `async` keyword cho lambda
- `await` cho service call
- `CancellationToken ct` — request bị abort → token cancel

### File 2: Async service

`OrderService.CreateAsync`:
```csharp
public async Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
{
    var products = await _db.Products
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, ct);

    // ... create order ...

    _db.Orders.Add(order);
    await _db.SaveChangesAsync(ct);

    return new OrderCreatedResponse(order.Id, order.OrderNumber, order.TotalAmount);
}
```

### File 3: CancellationToken propagation

```csharp
private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters, CancellationToken ct)
{
    try
    {
        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)
        );  // ← truyền ct xuống Dapper
        return rows.AsList();
    }
    catch (OperationCanceledException) { throw; }
}
```

→ `ct` được pass xuống từng level. User bấm cancel → ct cancel → SQL query bị abort.

## 5.4 Lỗi thường gặp

### Trap 1: async void

```csharp
// ❌ BAD — exception lost, không await được
public async void DoSomething() { ... }

// ✅ GOOD
public async Task DoSomething() { ... }
```

→ `async void` chỉ dùng cho event handler.

### Trap 2: Sync.Wait() trong async context = deadlock

```csharp
// ❌ DEADLOCK risk
public Task<int> Bad()
{
    var task = SomeAsync();
    return Task.FromResult(task.Result);  // ← block thread đang đợi async = deadlock
}

// ✅ OK
public async Task<int> Good()
{
    return await SomeAsync();
}
```

### Trap 3: Quên CancellationToken

```csharp
// ❌ User cancel request, query vẫn chạy → waste DB
await _db.Orders.ToListAsync();

// ✅ Propagate
await _db.Orders.ToListAsync(ct);
```

### Trap 4: ConfigureAwait(false) trong library code

```csharp
// Library code:
await SomeAsync().ConfigureAwait(false);  // skip SynchronizationContext
```

→ Trong ASP.NET Core (không có SyncContext) thì không cần. Trong WPF/WinForms thì cần.

## 5.5 OperationCanceledException

Khi `CancellationToken` cancel:
- Method đang await trả về throws `OperationCanceledException`
- `TaskCanceledException` là subclass của `OperationCanceledException`

Trong project, em handle:

```csharp
private async Task<IReadOnlyList<T>> QueryAsync<T>(...)
{
    try {
        var rows = await conn.QueryAsync<T>(...);
        return rows.AsList();
    }
    catch (OperationCanceledException) {
        throw;  // ack for VS debugger
    }
    catch (SqlException ex) when (ct.IsCancellationRequested) {
        throw new OperationCanceledException("Query cancelled", ex, ct);
    }
}
```

`GlobalExceptionHandler` convert `OperationCanceledException` → HTTP 499 (client closed request).

## 5.6 Interview Q&A

### Q1: "Async/await làm gì?"
> "Cho phép non-blocking I/O. Khi await, thread không sit idle đợi — nó được trả về thread pool để phục vụ request khác. Khi I/O xong, thread (có thể khác) tiếp tục method. Giúp scale tốt cho I/O-bound workload (DB, network)."

### Q2: "Khi nào dùng async, khi nào sync?"
> "Async cho I/O-bound (DB call, HTTP call, file). Sync cho CPU-bound (tính toán, parsing). Async không tự magic nhanh hơn — chỉ scale tốt hơn. CPU-bound dùng async + Task.Run thì OK."

### Q3: "Task.Result vs await?"
> "Task.Result block thread đến khi task done. Trong async context có thể deadlock. await không block — đăng ký callback. Luôn dùng await trong async method."

### Q4: "ConfigureAwait(false) làm gì?"
> "Skip capture SynchronizationContext khi resume. ASP.NET Core không có SyncContext nên không cần. WPF/WinForms hoặc library code thì nên dùng để tránh deadlock + nhanh hơn 1 chút."

### Q5: "Em handle cancellation thế nào trong project?"
> "Propagate CancellationToken từ endpoint xuống service xuống Dapper/EF. SQL query bị abort khi user cancel. Convert SqlException(cancelled) → OperationCanceledException. GlobalExceptionHandler trả 499 thay vì 500. Axios interceptor frontend cũng treat 499 = AbortError."

### Q6: "TaskCanceledException khác OperationCanceledException?"
> "TaskCanceledException inherit từ OperationCanceledException. Cùng cha cùng họ. Catch OperationCanceledException sẽ bắt cả 2. SqlClient có thể throw SqlException(0/-2) khi cancel — em convert sang OCE để xử lý đồng nhất."

## 5.7 Self-Test

### Bài 1 — Demo deadlock
```csharp
public Task<int> Bad()
{
    var t = Task.Run(async () => {
        await Task.Delay(1000);
        return 42;
    });
    return Task.FromResult(t.Result);  // sẽ deadlock trong WPF
}
```

### Bài 2 — Trace cancellation
1. Frontend: bấm 10 lần Refresh dashboard nhanh
2. VS log: thấy mỗi request cancel cái cũ
3. Console không có 500 ERR

### Bài 3 — Sync method trong async
Mở `BCrypt.Verify(...)` — đây là **sync** (CPU-bound). Em không thấy `await` cho nó. Đúng — không cần async.

## 5.8 Common Pitfalls

❌ `await task1; await task2;` khi 2 task độc lập
✅ `await Task.WhenAll(task1, task2);` — chạy song song

❌ async void
✅ async Task (trừ event handler)

❌ Sync wait inside async
✅ Always await

---

<a name="chapter-6"></a>
# 📖 Chapter 6 — SignalR Real-time

## 6.1 Intuition — Báo tin nhắn nhóm

**Polling** (cách cũ): Bạn cứ 30 giây gọi điện hỏi bạn thân "có gì mới không?" → tốn pin, nhiều cuộc gọi vô ích.

**Push** (SignalR): Bạn thân chủ động gọi khi có news. Bạn chỉ nhận khi cần.

→ SignalR = **WebSocket bidirectional**. Server có thể push event đến client, không cần client hỏi.

## 6.2 Core Concepts

### WebSocket vs HTTP

| | HTTP | WebSocket |
|---|---|---|
| Direction | Client → Server (request/response) | Bidirectional |
| Persistent | Đóng sau response | Mở liên tục |
| Overhead | ~700 bytes/request | Setup 1 lần, sau đó vài bytes/message |
| Use case | API calls, CRUD | Chat, notification, live updates |

### Hub pattern

```csharp
public class EtlNotificationHub : Hub
{
    // Client gọi method trên hub
    public async Task SendMessage(string msg) { ... }
    
    // Server gọi method trên client
    // (via Clients.All / Clients.Caller / Clients.User)
}
```

### Hub lifecycle

```
1. Client gọi /hub/etl
2. WebSocket upgrade (HTTP 101 Switching Protocols)
3. Connection persistent — Hub instance được tạo
4. Server có thể push event bất cứ lúc nào
5. Connection closed → Hub disposed
```

## 6.3 Code Walkthrough

### File 1: Hub class

`src/ECommerPipeline.Api/Hubs/EtlNotificationHub.cs`:

```csharp
public class EtlNotificationHub : Hub
{
    // Empty hub — server chỉ push, không nhận từ client
    // (project em không cần client gọi server qua hub)
}
```

### File 2: Hub registration

`Program.cs`:
```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<EtlNotificationHub>("/hub/etl");
```

### File 3: Backend push event

`src/ECommerPipeline.Api/Hubs/SignalREtlNotifier.cs`:

```csharp
public class SignalREtlNotifier : IEtlNotifier
{
    private readonly IHubContext<EtlNotificationHub> _hub;
    
    public Task NotifyCompletedAsync(EtlCompletedEvent evt, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync("etl-completed", evt, ct);
        //              ↑                   ↑
        //              all connected       event name + payload
    }
}
```

### File 4: ETL pipeline gọi notifier

`SalesEtlPipeline.RunAsync`:
```csharp
_logger.LogInformation("ETL done. Total rows: {Total}", totalProcessed);

await _notifier.NotifyCompletedAsync(new EtlCompletedEvent(
    TotalRowsProcessed: totalProcessed,
    Watermark: watermark,
    CompletedAt: DateTime.UtcNow,
    DurationMs: sw.ElapsedMilliseconds
), ct);
```

### File 5: Frontend client hook

`frontend/src/hooks/useEtlNotifications.ts`:

```typescript
export function useEtlNotifications(onEtlCompleted?: (evt: EtlCompletedEvent) => void) {
    const [status, setStatus] = useState<Status>('disconnected')

    useEffect(() => {
        const conn = new signalR.HubConnectionBuilder()
            .withUrl('/hub/etl')
            .withAutomaticReconnect()
            .build()

        // Listen to "etl-completed" event from server
        conn.on('etl-completed', (evt: EtlCompletedEvent) => {
            setLastEvent(evt)
            cbRef.current?.(evt)  // ← gọi callback
        })

        conn.start()
            .then(() => setStatus('connected'))
            .catch(err => setStatus('disconnected'))

        return () => { conn.stop() }
    }, [])

    return { status, lastEvent }
}
```

### File 6: Dashboard sử dụng hook

`pages/Dashboard.tsx`:
```typescript
const { status: signalRStatus, lastEvent } = useEtlNotifications(
    useCallback(() => { load(range) }, [load, range])
    // ↑ ETL xong → tự gọi load() reload data
)
```

→ Khi backend ETL xong, mọi user mở Dashboard đều thấy data refresh tự động.

## 6.4 Authentication với SignalR

Browsers KHÔNG cho set custom header trên WebSocket upgrade. Workaround:

```typescript
// Frontend
new HubConnectionBuilder()
    .withUrl('/hub/etl', {
        accessTokenFactory: () => session.accessToken  // ← truyền qua query string
    })
    .build()
```

```csharp
// Backend Program.cs
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
```

## 6.5 Nginx WebSocket proxy

Project Docker dùng Nginx proxy. Cần config đặc biệt cho WebSocket upgrade:

```nginx
location /hub/ {
    proxy_pass         http://api:8080;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade           $http_upgrade;
    proxy_set_header   Connection        "upgrade";   # ← magic
    proxy_set_header   Host              $host;
    proxy_read_timeout 86400s;  # WebSocket long-lived
}
```

## 6.6 Interview Q&A

### Q1: "SignalR khác polling thế nào?"
> "Polling: client hỏi server mỗi N giây. SignalR: persistent connection, server chủ động push. Polling tốn bandwidth (mỗi 30s 1 HTTP request) + latency cao (N/2 giây trung bình). SignalR push instant + ít tốn. Em dùng cho notification khi ETL xong."

### Q2: "SignalR underlying transport?"
> "WebSocket là first choice. Fallback Server-Sent Events (SSE) hoặc Long Polling nếu network không support WS. Em không phải config, SignalR tự negotiate khi connect."

### Q3: "Hub vs persistent connection?"
> "Hub là abstraction trên SignalR. Cho phép gọi method từ server đến client như RPC. Persistent connection là raw stream. Hub dễ dùng hơn — em dùng Hub."

### Q4: "Authentication với WebSocket?"
> "Browser không set Authorization header trên WS upgrade. Workaround: truyền access_token qua query string. Backend JwtBearer.Events.OnMessageReceived catch nó, set ctx.Token. SignalR connect như thường."

### Q5: "Em scale SignalR thế nào với nhiều server?"
> "1 server: in-memory state OK. Nhiều server: cần backplane (Redis pub/sub). Server A push event → Redis → mọi server khác đẩy đến client của mình. Project em demo 1 server nên chưa setup Redis."

## 6.7 Self-Test

### Bài 1 — Quan sát WebSocket
1. Mở DevTools → Network tab → WS filter
2. Load Dashboard
3. Thấy connection `/hub/etl?access_token=...`
4. Status: 101 Switching Protocols
5. Tab Messages — thấy ping/pong + event payload

### Bài 2 — Trigger ETL → quan sát SignalR push
1. Mở Dashboard tab 1
2. Tab 2: /admin/stress → Trigger ETL
3. Tab 1: badge "SignalR: connected" có thông tin lastEvent
4. KPIs auto refresh

### Bài 3 — Kill connection
1. DevTools → Network → WS → right-click → Block request URL
2. Reload → status "reconnecting" sau vài giây
3. Unblock → "connected"

## 6.8 Common Pitfalls

❌ Nginx không config WS upgrade → connection fail
✅ `proxy_set_header Upgrade $http_upgrade; Connection "upgrade";`

❌ Cookie auth + WS → CORS issue
✅ Bearer query string

❌ Bỏ `.withAutomaticReconnect()` → mất connection vĩnh viễn khi network blip
✅ Always reconnect

---

<a name="chapter-7"></a>
# 📖 Chapter 7 — Hangfire Background Jobs

## 7.1 Intuition — Hộp thư công việc văn phòng

Văn phòng có hộp thư:
- Sếp viết note bỏ vào hộp ("In báo cáo này")
- Cuối ngày, anh nhân viên kiểm hộp, làm từng note
- Note xong → đánh dấu done
- Note lỗi → để lại, ngày mai làm lại

→ Hangfire = hệ thống "hộp thư" cho async background jobs.

## 7.2 Core Concepts

### Job types

| Loại | Khi nào chạy |
|---|---|
| **Fire-and-forget** | Ngay khi enqueue |
| **Delayed** | Sau X phút/giờ/ngày |
| **Recurring** | Theo cron expression |
| **Continuations** | Sau khi job trước done |

### Storage

Hangfire lưu state vào DB. Project dùng SQL Server table riêng (`ECommerPipeline_Hangfire`):
- Jobs queue
- Job history (succeeded/failed)
- Server registry
- Locks (cho DisableConcurrentExecution)

### Dashboard

`/hangfire` URL có UI built-in:
- Recurring Jobs (cron schedule)
- Succeeded / Failed jobs
- Servers info
- Manually trigger job

## 7.3 Code Walkthrough

### File 1: Job class

`src/ECommerPipeline.Infrastructure/Etl/EtlJob.cs`:

```csharp
public class EtlJob
{
    private readonly IEtlPipeline _pipeline;
    private readonly ResiliencePipeline _retry;

    public EtlJob(IEtlPipeline pipeline, ILogger<EtlJob> logger)
    {
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct)
    {
        await _retry.ExecuteAsync(async token => await _pipeline.RunAsync(token), ct);
    }
}
```

**Để ý:**
- `[DisableConcurrentExecution]` — chống 2 instance cùng chạy
- Polly retry policy 3 lần exponential backoff
- `CancellationToken` cho graceful shutdown

### File 2: Hangfire DI

`DependencyInjection.cs`:

```csharp
services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(config.GetConnectionString("HangfireConnection"),
        new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
            QueuePollInterval = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        }));

services.AddHangfireServer();
```

### File 3: Recurring jobs

```csharp
public static void RegisterRecurringJobs(IServiceProvider sp)
{
    var manager = sp.GetRequiredService<IRecurringJobManager>();

    manager.AddOrUpdate<EtlJob>(
        "sales-etl",
        j => j.RunAsync(CancellationToken.None),
        "*/5 * * * *");  // every 5 minutes

    manager.AddOrUpdate<CompressColumnstoreJob>(
        "compress-columnstore",
        j => j.RunAsync(CancellationToken.None),
        "0 2 * * *");  // 2AM daily
}
```

### File 4: Enqueue ngay

```csharp
app.MapPost("/api/admin/trigger-etl", (IBackgroundJobClient jobs) =>
{
    var jobId = jobs.Enqueue<EtlJob>(j => j.RunAsync(CancellationToken.None));
    return Results.Accepted(value: new
    {
        status   = "etl-enqueued",
        jobId,
    });
});
```

## 7.4 Cron expressions

```
┌───────── minute (0-59)
│ ┌─────── hour (0-23)
│ │ ┌───── day of month (1-31)
│ │ │ ┌─── month (1-12)
│ │ │ │ ┌─ day of week (0-6, Sun=0)
│ │ │ │ │
*/5 * * * *   → every 5 minutes
0 2 * * *     → 2:00 AM daily
0 0 1 * *     → midnight 1st of month
0 9 * * 1-5   → 9 AM weekdays
```

## 7.5 [DisableConcurrentExecution]

```csharp
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public async Task RunAsync(CancellationToken ct) { ... }
```

Hangfire dùng distributed lock trong DB. Khi 1 instance đang chạy, instance khác:
- Cùng job name → đợi (timeout 600s)
- Timeout → throw

→ Tránh race condition khi recurring fire + manual trigger.

## 7.6 Retry mechanics

### Built-in Hangfire retry
Job throws exception → Hangfire tự retry 10 lần với exponential backoff. Mỗi attempt lưu lịch sử trong DB. Dashboard show.

### Polly retry trong job (em dùng)
Catch transient SQL error → retry trong cùng 1 attempt → không tạo nhiều record trong history.

→ Combo: Polly handle transient nhanh, Hangfire handle hard failure.

## 7.7 Interview Q&A

### Q1: "Hangfire khác Quartz?"
> "Hangfire có dashboard built-in, lưu state SQL Server, dễ setup. Quartz mạnh hơn (clustering, listener API), nhưng config phức tạp. Project em dùng Hangfire vì cần recurring job + history + dashboard."

### Q2: "BackgroundService trong .NET là gì? Khác Hangfire?"
> "BackgroundService chạy in-process, không persist state. App restart → mất queue. Hangfire persist DB → restart không mất. BackgroundService phù hợp continuous loop (vd: poll message queue). Hangfire phù hợp discrete jobs có schedule."

### Q3: "Em xử lý race condition giữa 2 instance ETL?"
> "DisableConcurrentExecution attribute. Hangfire dùng distributed lock SQL Server. 2 worker cùng pull job → 1 chạy, 1 đợi. Timeout 600s. Cộng thêm MERGE WITH HOLDLOCK ở SQL level cho atomic upsert."

### Q4: "Em monitor jobs thế nào?"
> "Hangfire Dashboard tại /hangfire. Xem: Recurring Jobs (cron list), Succeeded/Failed (history với stack trace), Servers info. Set up alerting qua webhook khi Failed > N (chưa làm trong project demo)."

### Q5: "Job timeout là gì?"
> "Hangfire có 2 timeout: SlidingInvisibilityTimeout (job lock duration) + DisableConcurrentExecution timeout. Nếu job chạy quá lâu (vd ETL 30 phút), tăng SlidingInvisibilityTimeout. Em set 5 phút default."

## 7.8 Self-Test

### Bài 1 — Trigger thủ công
1. Mở /hangfire
2. Recurring Jobs → tìm sales-etl
3. Click Trigger Now
4. Xem nó move sang Processing → Succeeded

### Bài 2 — Force fail + retry
1. Stop SQL Server
2. Trigger ETL → fail
3. Đợi 1 phút → Hangfire tự retry → fail
4. Start lại SQL → next retry success
5. Xem history trong dashboard

### Bài 3 — Cron expression
- 0 9 * * 1 = ?
- */15 * * * * = ?
- 0 0 1 1 * = ?

Trả lời:
- 9 AM thứ 2 hàng tuần
- mỗi 15 phút
- 0:00 ngày 1 tháng 1 hàng năm

## 7.9 Common Pitfalls

❌ Inject scoped service vào singleton job → lifetime mismatch
✅ Hangfire tự tạo scope cho mỗi job invocation

❌ Lưu state in-memory trong job class
✅ Stateless — mọi data từ DB hoặc parameter

---

<a name="chapter-8"></a>
# 📖 Chapter 8 — React Hooks + Context

## 8.1 Intuition — Sổ ghi nhớ cá nhân vs bảng thông báo công ty

**useState** = sổ ghi nhớ riêng của 1 component. Component khác không thấy.

**Context** = bảng thông báo treo trên tường — mọi component trong cây React đều đọc được.

→ State local nhỏ → useState. State chia sẻ nhiều page → Context (vd auth, cart).

## 8.2 Core Hooks

### useState

```typescript
const [count, setCount] = useState(0);
// count: giá trị hiện tại
// setCount: hàm cập nhật
```

### useEffect

```typescript
useEffect(() => {
    // chạy sau render
    fetchData();
    
    return () => {
        // cleanup khi unmount hoặc dep thay đổi
    };
}, [dependency]);  // chạy lại khi dep thay đổi
```

### useCallback

```typescript
const memoized = useCallback(() => doSomething(x), [x]);
// memoized chỉ tạo function mới khi x thay đổi
// → tránh re-render con không cần thiết
```

### useRef

```typescript
const inputRef = useRef<HTMLInputElement>(null);
// inputRef.current = DOM element
// Hoặc lưu value mutable không trigger re-render
```

### useContext

```typescript
const { user, login } = useContext(AuthContext);
```

## 8.3 Code Walkthrough

### File 1: AuthContext

`frontend/src/contexts/AuthContext.tsx`:

```typescript
const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
    const [session, setSession] = useState<AuthSession | null>(loadSession);

    useEffect(() => {
        if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
        else localStorage.removeItem(STORAGE_KEY);
    }, [session]);

    const login = async (email: string, password: string) => {
        applyTokens(await authApi.login({ email, password }));
    };

    return (
        <AuthContext.Provider value={{ session, user, login, ... }}>
            {children}
        </AuthContext.Provider>
    );
}

export function useAuth() {
    const ctx = useContext(AuthContext);
    if (!ctx) throw new Error('useAuth must be used within AuthProvider');
    return ctx;
}
```

**Để ý:**
- Provider wrap toàn bộ app (xem App.tsx)
- `useEffect` sync state vào localStorage
- Custom hook `useAuth()` wrap useContext + null check

### File 2: useEtlNotifications custom hook

```typescript
export function useEtlNotifications(onEtlCompleted?: (evt: EtlCompletedEvent) => void) {
    const [status, setStatus] = useState<Status>('disconnected');
    
    // Keep latest callback in ref to avoid effect re-run
    const cbRef = useRef(onEtlCompleted);
    useEffect(() => { cbRef.current = onEtlCompleted; }, [onEtlCompleted]);

    useEffect(() => {
        const conn = new HubConnectionBuilder()...build();
        conn.on('etl-completed', (evt) => cbRef.current?.(evt));
        
        let cancelled = false;
        conn.start().then(() => { if (!cancelled) setStatus('connected'); });
        
        return () => {
            cancelled = true;
            conn.stop();
        };
    }, []);  // ← empty deps = chỉ chạy 1 lần khi mount

    return { status, lastEvent };
}
```

**Pattern quan trọng:** dùng ref để store callback latest mà KHÔNG cần re-create effect.

### File 3: useEffect cleanup

`Dashboard.tsx`:
```typescript
useEffect(() => {
    const ctrl = new AbortController();
    load(range, ctrl.signal);
    return () => ctrl.abort();
}, [load, range]);
```

→ Khi range thay đổi, cleanup abort request cũ rồi mới start cái mới. Tránh race condition.

### File 4: useCallback prevent re-render

```typescript
const load = useCallback(async (r: DateRange, signal?: AbortSignal) => {
    // ... fetch logic
}, []);  // empty deps = function instance never changes

useEffect(() => {
    load(range);  // load không là dep "moving target"
}, [load, range]);
```

→ Không có useCallback, mỗi render `load` là instance mới → useEffect chạy lại → infinite loop.

## 8.4 Dependency Array — quan trọng nhất

```typescript
useEffect(() => { ... }, []);              // chạy 1 lần khi mount
useEffect(() => { ... }, [a, b]);          // chạy lại khi a hoặc b đổi
useEffect(() => { ... });                  // chạy SAU MỖI render (rare)
```

**Quy tắc vàng:** Mọi value bạn dùng INSIDE effect phải có trong deps array. ESLint rule `react-hooks/exhaustive-deps` enforce.

## 8.5 StrictMode

```typescript
// main.tsx
<StrictMode>
    <App />
</StrictMode>
```

Trong dev, StrictMode chạy:
- Effect mount → cleanup → mount lại (double invocation)
- Để phát hiện effect không cleanup đúng

→ Hook em phải robust với race condition (vd cancelled flag trong useEtlNotifications).

## 8.6 Interview Q&A

### Q1: "useState vs Context khác nhau?"
> "useState là local state — chỉ component đó. Context share state qua tree component không cần prop drilling. Project em dùng useState cho UI state cục bộ (form field, filter), Context cho global (auth, cart)."

### Q2: "useEffect deps array làm gì?"
> "Khai báo value mà effect phụ thuộc. Effect chạy lại khi deps thay đổi. Empty array = chỉ mount/unmount. Không có array = sau mỗi render. ESLint enforce exhaustive deps tránh stale closure."

### Q3: "Khi nào dùng useCallback?"
> "Khi pass function xuống component con đã memoized, hoặc đưa function vào deps array của effect. Tránh function instance mới mỗi render. Không phải mọi function đều cần memoize — overhead chính."

### Q4: "useRef khác useState ra sao?"
> "useState trigger re-render khi update. useRef KHÔNG. Dùng useRef khi cần mutable value không cần render (vd lưu interval id, DOM ref, latest callback). Update ref.current không gây render."

### Q5: "Custom hook là gì?"
> "Function tên bắt đầu bằng 'use', return state + functions. Encapsulate logic dùng được nhiều component. Project em có useEtlNotifications, useAuth, useCart. Tách logic khỏi UI, dễ test."

### Q6: "StrictMode side-effect?"
> "Dev mode chạy effect 2 lần để phát hiện không cleanup. Production chỉ chạy 1 lần. Em phải viết effect idempotent: track 'cancelled' flag, abort fetch trong cleanup, không leak listener."

## 8.7 Self-Test

### Bài 1 — Trace useEffect
Mở Dashboard.tsx. Cho 2 effect (1 cho load data, 1 cho SignalR). Trace:
- Mount → effect nào chạy trước
- Range thay đổi → effect nào re-run
- Unmount → cleanup nào chạy

### Bài 2 — Custom hook
Viết hook `useDebounce(value, delay)` return value debounce sau delay ms. Test với search input.

### Bài 3 — Context bypass
Tạo component dùng useAuth nhưng KHÔNG wrap trong AuthProvider. → Error throw "must be used within AuthProvider". Hiểu vì sao.

## 8.8 Common Pitfalls

❌ Missing dependency trong useEffect
✅ Add tất cả vào deps, hoặc dùng useRef cho non-render value

❌ Set state trong render → infinite loop
✅ Set state trong effect hoặc handler

❌ Forget cleanup → memory leak
✅ Return cleanup function trong effect

---

<a name="chapter-9"></a>
# 📖 Chapter 9 — Docker Compose

## 9.1 Intuition — Lego cho server

**Docker container** = 1 viên Lego có sẵn (1 service: SQL, API, frontend).
**Compose** = bộ Lego có hướng dẫn ráp.

→ `docker compose up` = ráp xong cả lâu đài (full stack) trong 1 lệnh.

## 9.2 Core Concepts

### Image vs Container

- **Image** = blueprint (template). Build 1 lần từ Dockerfile.
- **Container** = instance đang chạy của image. Run nhiều container từ 1 image.

### Dockerfile vs docker-compose.yml

- **Dockerfile** = công thức build 1 image
- **docker-compose.yml** = orchestration nhiều container

### Volume

Container ephemeral — chết là mất hết. Volume = persistent storage gắn ngoài container:
```yaml
volumes:
  sql_data:  # named volume
```
→ SQL Server data lưu vào volume, container restart không mất.

### Network

Compose tự tạo network. Container talk nhau qua tên service:
```yaml
services:
  api:
    environment:
      ConnectionStrings__OltpConnection: "Server=sql;..."  # ← "sql" là tên service
```

### Healthcheck

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -fsS http://localhost:8080/health || exit 1"]
  interval: 15s
  retries: 8
  start_period: 60s
```

→ Container chạy nhưng "unhealthy" nếu healthcheck fail. depends_on có thể đợi healthy.

## 9.3 Code Walkthrough

### File 1: API Dockerfile

`src/ECommerPipeline.Api/Dockerfile`:

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ECommerPipeline.sln ./
COPY src/ECommerPipeline.Domain/ECommerPipeline.Domain.csproj src/ECommerPipeline.Domain/
# ... (chỉ copy csproj trước để leverage cache)
RUN dotnet restore src/ECommerPipeline.Api/ECommerPipeline.Api.csproj

COPY src/ src/
RUN dotnet publish src/ECommerPipeline.Api/ECommerPipeline.Api.csproj \
    -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=app:app /app/publish .
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ECommerPipeline.Api.dll"]
```

**Multi-stage build:** stage 1 dùng SDK (lớn, có compiler). Stage 2 dùng runtime (nhỏ, chỉ có CLR). Image cuối chỉ chứa output build, không có source/SDK → image gọn hơn 5-10×.

### File 2: Frontend Dockerfile

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --legacy-peer-deps
COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

### File 3: docker-compose.yml

```yaml
services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStrong@Passw0rd"
    volumes:
      - sql_data:/var/opt/mssql  # ← persistent
    healthcheck:
      test: [...]

  api:
    build:
      context: .
      dockerfile: src/ECommerPipeline.Api/Dockerfile
    depends_on:
      sql:
        condition: service_healthy  # ← đợi SQL healthy mới start
    environment:
      ConnectionStrings__OltpConnection: "Server=sql;..."

  frontend:
    build: ./frontend
    depends_on:
      api:
        condition: service_started
    ports:
      - "80:80"  # ← expose ra host

volumes:
  sql_data:
```

### File 4: Nginx config

```nginx
location /api/ {
    proxy_pass http://api:8080;  # ← "api" là service name
}

location /hub/ {
    proxy_pass http://api:8080;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
}
```

## 9.4 Workflow

```bash
docker compose up -d              # start
docker compose ps                 # check status
docker compose logs -f api        # tail logs
docker compose exec api bash      # vào shell container
docker compose down               # stop
docker compose down -v            # stop + wipe volumes
docker compose up -d --build      # rebuild image
```

## 9.5 Interview Q&A

### Q1: "Image vs Container?"
> "Image là blueprint immutable build từ Dockerfile. Container là instance đang chạy. 1 image → nhiều container. Container có state runtime (filesystem changes), image không."

### Q2: "Vì sao multi-stage build?"
> "Stage build dùng SDK to (~1GB) để compile. Stage runtime dùng ASP.NET image nhỏ (~200MB) chỉ có CLR. Output binary copy từ build → runtime. Final image gọn hơn, security tốt hơn (không có compiler trong prod)."

### Q3: "Volume vs bind mount?"
> "Volume managed by Docker (location ẩn). Bind mount link host folder. Volume portable, bind mount tốt cho dev (sửa code không cần rebuild). Project em dùng volume cho SQL data persist."

### Q4: "depends_on có đợi service ready không?"
> "Default chỉ đợi container start (process spawned), KHÔNG đợi service ready. Phải dùng `condition: service_healthy` + healthcheck mới đợi đến khi /health pass."

### Q5: "Network mode?"
> "Default bridge — container có IP riêng, talk qua service name. Host mode — share network với host (production rare). Project em dùng bridge."

### Q6: "Em deploy production thế nào?"
> "Compose OK cho dev/single-server. Production cần orchestrator như Kubernetes. K8s thêm: rolling update, auto-scaling, service discovery, secrets management. Project em chưa cần K8s vì single-server demo."

## 9.6 Self-Test

### Bài 1 — Inspect
```bash
docker compose ps                       # services
docker inspect ecom-api                 # full config
docker compose logs --tail 50 api       # recent logs
```

### Bài 2 — Exec vào container
```bash
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd' -No
# Trong sqlcmd:
1> SELECT name FROM sys.databases
2> GO
```

### Bài 3 — Rebuild khi sửa code
```bash
# Sửa Program.cs
docker compose up -d --build api  # rebuild chỉ api
docker compose logs -f api        # verify
```

## 9.7 Common Pitfalls

❌ `docker-compose` (cũ, hyphen) vs `docker compose` (mới, space)
✅ Mới hơn dùng `docker compose`

❌ Quên `-d` → docker compose up chạy foreground, Ctrl+C kill
✅ Always `-d` cho long-running

❌ Sửa code không rebuild → vẫn chạy code cũ
✅ `--build` flag

---

<a name="chapter-10"></a>
# 📖 Chapter 10 — Tailwind + Tremor

## 10.1 Intuition — Lego CSS

**CSS truyền thống:** Bạn vẽ component (định nghĩa class), styling riêng.
```css
.my-card { padding: 16px; background: #1f2937; border-radius: 8px; }
```

**Tailwind:** Bạn ráp utility classes có sẵn.
```html
<div class="p-4 bg-gray-800 rounded-lg">
```

→ Không phải đặt tên class, không phải maintain CSS file, không có dead CSS.

## 10.2 Tailwind core

### Spacing scale
```
p-1 = 4px    p-4 = 16px    p-8 = 32px
m-2 = 8px    gap-3 = 12px
```

### Colors
```
bg-blue-500     text-gray-100     border-gray-800
bg-rose-900/40  ← alpha 40%
```

### Responsive
```html
<div class="grid grid-cols-1 md:grid-cols-3">
<!-- 1 col mobile, 3 col từ md (768px) trở lên -->
```

### Dark mode
```html
<body class="bg-white dark:bg-gray-950">
<!-- Default white, khi html có class="dark" thì gray-950 -->
```

## 10.3 Tremor — BI dashboard library

Tremor là set component built trên Tailwind, chuyên cho BI dashboard:

```tsx
<Card decoration="top" decorationColor="blue">
    <Text>Total Revenue</Text>
    <Metric>{formatVnd(totalRevenue)}</Metric>
</Card>

<AreaChart
    data={chartData}
    index="date"
    categories={['Revenue', 'Orders']}
    colors={['blue', 'emerald']}
/>

<DonutChart data={donutData} category="sales" index="name" />

<BarList data={topList} valueFormatter={formatVnd} />
```

## 10.4 Code Walkthrough

### File 1: Tailwind config

`frontend/tailwind.config.js`:

```javascript
export default {
  darkMode: 'class',  // ← bật khi html có class="dark"
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
    './node_modules/@tremor/**/*.{js,ts,jsx,tsx}',  // ← include Tremor source
  ],
  theme: {
    extend: {
      colors: {
        'dark-tremor': {
          background: { DEFAULT: '#111827' },
          content: { emphasis: '#e5e7eb', strong: '#f9fafb' },
        },
      },
    },
  },
  safelist: [
    // Pattern cho dynamic class (Tremor sinh ra)
    { pattern: /^(bg|text|border)-(blue|emerald|...)-(500|600)$/ },
  ],
}
```

### File 2: Dashboard

`pages/Dashboard.tsx`:
```tsx
<div className="p-6 space-y-6">
    <Grid numItemsMd={3} className="gap-6">
        <Card decoration="top" decorationColor="blue">
            <Text>Total Revenue</Text>
            <Metric>{formatVnd(totalRevenue)}</Metric>
        </Card>
        {/* ... 2 cards khác */}
    </Grid>

    <Card>
        <Title>Sales by Day</Title>
        <AreaChart
            className="h-72 mt-4"
            data={chartData}
            index="date"
            categories={['Revenue', 'Orders']}
            colors={['blue', 'emerald']}
        />
    </Card>
</div>
```

### File 3: Dark mode override

`src/index.css`:
```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@layer components {
    .dark .tremor-Title-root { @apply text-gray-50; }
    .dark .tremor-Text-root  { @apply text-gray-300; }
    .dark .tremor-Card-root  { @apply bg-gray-900 border-gray-800; }
}
```

## 10.5 Interview Q&A

### Q1: "Tailwind vs Bootstrap?"
> "Bootstrap có pre-built components (Card, Button). Tailwind là utility-first, bạn ráp design tự do. Tailwind output CSS chỉ classes dùng (purge unused). Project nhỏ Bootstrap nhanh hơn, project có design unique Tailwind tốt hơn."

### Q2: "Vì sao dùng Tremor?"
> "Tremor là Tailwind components cho BI dashboard: Card, Metric, AreaChart, DonutChart, BarList, Table. Built-in dark mode + responsive. Em tiết kiệm thời gian custom UI charts. Underlying là Recharts."

### Q3: "Dynamic class trong Tailwind work thế nào?"
> "Tailwind purge unused class ở build time. Dynamic class như `bg-${color}-500` sẽ bị purge nếu không thấy trong code. Fix bằng safelist trong config — pattern match các class dynamic."

### Q4: "darkMode: 'class' vs 'media'?"
> "'media' theo prefers-color-scheme browser. 'class' khi html có class='dark'. Em chọn 'class' để control thủ công (force dark mode cho consistency)."

### Q5: "Performance Tailwind so với CSS thường?"
> "Build time CSS Tailwind generates lớn nhưng PurgeCSS xoá class không dùng → bundle gọn (vài KB sau gzip). Runtime tương đương CSS thường. Slight build overhead lúc dev."

## 10.6 Self-Test

### Bài 1 — Inspect class
1. Mở /admin Dashboard
2. F12 → click KPI card → xem class:
   - `tremor-Card-root` (Tremor's class)
   - + utility classes (`p-6`, `bg-gray-900`, etc.)

### Bài 2 — Custom theme
Trong tailwind.config.js, thử đổi `dark-tremor.background.DEFAULT` từ `#111827` sang `#1a1a2e`. Reload → thấy card background đổi màu.

### Bài 3 — Responsive test
1. F12 → Toggle device toolbar
2. Test mobile (375px) → sidebar có collapse không?
3. Tablet (768px) → grid layout đổi 1 col → 3 col

## 10.7 Common Pitfalls

❌ `className={`bg-${color}-500`}` → purge xoá → không hiện
✅ Safelist trong config

❌ Conflict với global CSS
✅ Tailwind preflight reset đã handle

❌ Dùng `style={{}}` thay class → mất lợi ích Tailwind
✅ Stick to className

---

# 🎓 Lộ trình học 12 ngày

| Ngày | Chapter | Goal |
|---|---|---|
| 1-2 | OLTP/OLAP + ETL | Vẽ được kiến trúc, trace SalesEtlPipeline.cs |
| 3 | Clean Architecture | Hiểu 4 layer + dependency flow |
| 4 | JWT Authentication | Decode token, hiểu refresh flow |
| 5-6 | EF Core | Migration, LINQ→SQL, AsNoTracking |
| 7 | Async/Await | Hiểu cancellation propagation |
| 8 | SignalR | Trigger ETL + quan sát push |
| 9 | Hangfire | Trigger Now, hiểu cron |
| 10 | React Hooks | Trace useEffect/useCallback |
| 11 | Docker | docker compose up/down/exec |
| 12 | Tailwind + ôn tập | Tweak theme + tổng kết |

## Sau 12 ngày

- ✅ Trả lời confident 50+ câu hỏi phỏng vấn
- ✅ Vẽ được kiến trúc trên giấy không cần look up
- ✅ Trace code project bằng tay
- ✅ Defend được mọi quyết định kỹ thuật
- ✅ Sẵn sàng apply intern / junior backend

## Tips học

1. **Không skip chapter** dù tự tin
2. **Mở code project song song** khi đọc — luôn link concept với code thật
3. **Tự trả lời Q&A** trước khi đọc model answer
4. **Ghi notes tay** — viết tay giúp nhớ tốt hơn đọc
5. **Mock interview** với bạn / mentor sau ngày 7

---

**Chúc bạn thành công! 🚀**

Khi nào học xong chapter nào → gửi câu hỏi cụ thể bạn vướng → tôi giải thích sâu hơn cái đó.
