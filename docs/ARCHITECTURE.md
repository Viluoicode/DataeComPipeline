# 🏛 Enterprise Architecture — ECommerPipeline

> Tài liệu này giải thích **các pattern enterprise** đã apply vào project sau Phase E (Enterprise upgrades).
> Đây là phần **sát thực tế doanh nghiệp** — recruiter senior sẽ ấn tượng vì hiểu được data warehouse patterns.

---

## 📑 Mục lục

1. [Tổng quan kiến trúc](#overview)
2. [Medallion Architecture (Bronze / Silver / Gold)](#medallion)
3. [SCD Type 2 (Slowly Changing Dimensions)](#scd2)
4. [Data Quality Framework](#data-quality)
5. [OpenTelemetry Distributed Tracing](#otel)
6. [So sánh với enterprise reality](#enterprise-comparison)

---

<a name="overview"></a>
## 1. Tổng quan kiến trúc

```
┌────────────────────────────────────────────────────────────────────┐
│                         APPLICATION LAYER                          │
│  React SPA → ASP.NET Core (JWT + SignalR)                          │
└──────────────────────────────┬─────────────────────────────────────┘
                               │
            ┌──────────────────┼────────────────────┐
            │                  │                    │
            ▼                  ▼                    ▼
       OLTP write         OLAP read           Real-time push
            │                  │                    ▲
            │                  │                    │
            ▼                  ▼                    │
┌────────────────────┐ ┌────────────────────────────┴───────────┐
│ OLTP DATABASE      │ │  OLAP DATABASE — Medallion Architecture │
│ (Row-store)        │ │                                         │
│                    │ │  ┌─────────────────────────────────┐    │
│ • Customers        │ │  │ 🥉 BRONZE                       │    │
│ • Products         │ │  │ bronze.OrderItem_Raw            │    │
│ • Orders           │ │  │ (raw landing, no transformation)│    │
│ • OrderItems       │ │  └────────────┬────────────────────┘    │
│ • RefreshTokens    │ │               │                          │
└──────────┬─────────┘ │               ▼                          │
           │           │  ┌─────────────────────────────────┐    │
           │  ETL job  │  │ 🥈 SILVER (Star Schema)          │    │
           ├──────────►│  │ dim.Customer (SCD Type 2)        │    │
           │ (every    │  │ dim.Product  (SCD Type 2)        │    │
           │  5 min)   │  │ dim.Date                         │    │
           │           │  │ fact.SalesOrderItem (Columnstore)│    │
           │           │  └────────────┬────────────────────┘    │
           │           │               │                          │
           │           │               ▼                          │
           │           │  ┌─────────────────────────────────┐    │
           │           │  │ 🥇 GOLD (Pre-aggregated)         │    │
           │           │  │ gold.DailySalesByCategory        │    │
           │           │  │ gold.MonthlyTopProducts          │    │
           │           │  │ gold.CustomerLifetimeValue       │    │
           │           │  └─────────────────────────────────┘    │
           │           └─────────────────────────────────────────┘
           │
           │                ┌──────────────────────────────────┐
           │                │  Data Quality Tests              │
           ├───────────────►│  dq.TestResults                  │
           │                │  (11 tests, every 15 min)        │
           │                └──────────────────────────────────┘
           │
           │                ┌──────────────────────────────────┐
           └───────────────►│  OpenTelemetry → Jaeger          │
                            │  (distributed tracing)           │
                            └──────────────────────────────────┘
```

### Các thành phần chính

| Component | Vai trò | Schedule |
|---|---|---|
| **SalesEtlPipeline** | Extract OLTP → Bronze + Silver + Gold | Every 5 min |
| **CompressColumnstoreJob** | REORGANIZE WITH COMPRESS rowgroups | 2 AM daily |
| **DataQualityJob** | 11 tests on OLAP, alert if critical fail | Every 15 min |
| **SignalR Hub** | Push `etl-completed` + `dq-alert` events | Real-time |
| **OpenTelemetry** | Traces every request + ETL run to Jaeger | Real-time |

---

<a name="medallion"></a>
## 2. Medallion Architecture

### 2.1 Vấn đề khi không có

Star schema 1 layer (chỉ fact + dimensions):
- Query analytical phức tạp (vd "doanh thu theo category 90 ngày") phải recompute mỗi lần → chậm
- Không thể replay/audit raw data (nếu transformation logic sai, mất gốc)
- Khó debug khi data anomaly xảy ra

### 2.2 Medallion 3 tầng

#### 🥉 Bronze — Raw Landing Zone

```sql
CREATE TABLE bronze.OrderItem_Raw (
    BronzeKey         BIGINT IDENTITY PRIMARY KEY,
    OrderItemId       BIGINT NOT NULL,
    OrderId           BIGINT NOT NULL,
    CustomerId        BIGINT NOT NULL,    -- giữ ID gốc, không lookup
    ProductId         BIGINT NOT NULL,
    OrderDate         DATETIME2 NOT NULL,
    Quantity          INT NOT NULL,
    UnitPrice         DECIMAL(18,2) NOT NULL,
    LineTotal         DECIMAL(18,2) NOT NULL,
    IngestedAt        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    SourceSystem      VARCHAR(50) NOT NULL DEFAULT 'OLTP_EFCore'
);
```

**Mục đích:**
- Lưu **y nguyên data** từ source (1:1 copy)
- Không transformation, không lookup
- Source of truth cho replay/backfill
- Multiple sources có thể đổ vào (Shopee, Tiki, manual upload) — track qua `SourceSystem`

**Pattern này tại doanh nghiệp:**
- Tiki/Lazada: S3 raw bucket lưu JSON từ Kafka
- Snowflake: VARIANT column hoặc external table
- Databricks: Delta Lake bronze tables

#### 🥈 Silver — Cleaned + Conformed

```sql
fact.SalesOrderItem  (Columnstore, ~300k rows, surrogate keys)
dim.Customer         (SCD Type 2, history preserved)
dim.Product          (SCD Type 2)
dim.Date             (1095 rows for 3 years)
```

**Mục đích:**
- Cleaned: validated, type-cast, deduplicated
- Conformed: dùng surrogate keys, star schema
- Sẵn sàng cho ad-hoc analytics

#### 🥇 Gold — Business-Ready

```sql
gold.DailySalesByCategory      -- pre-aggregate doanh thu theo ngày × category
gold.MonthlyTopProducts        -- top 50 product mỗi tháng (window function)
gold.CustomerLifetimeValue     -- LTV snapshot
```

**Mục đích:**
- Pre-aggregated cho dashboard query siêu nhanh
- Schema match đúng business question
- Dashboard query Gold (5-10ms) thay vì recompute từ fact (90ms)

**Trong project:** Reports endpoints đã chuyển từ query Silver → Gold:
```csharp
// Before (Silver): SUM + GROUP BY trên 300k fact rows
SELECT p.Category, COUNT(DISTINCT f.OrderId), SUM(f.LineTotal)
FROM fact.SalesOrderItem f JOIN dim.Product p ON ...
GROUP BY p.Category;

// After (Gold): chỉ SUM trên ~700 daily aggregates  
SELECT Category, SUM(OrderCount), SUM(TotalRevenue)
FROM gold.DailySalesByCategory
WHERE Date BETWEEN @From AND @To
GROUP BY Category;
```

→ Query nhanh hơn **10-20×**.

### 2.3 Refresh strategy

Silver được load **incrementally** (chỉ delta qua watermark).
Gold được **full refresh** sau mỗi ETL run (TRUNCATE + INSERT). Lý do:
- Gold tables nhỏ (vài nghìn rows)
- Logic đơn giản, không cần incremental
- Production với >10M rows mới cần incremental refresh per partition

---

<a name="scd2"></a>
## 3. SCD Type 2 — Slowly Changing Dimensions

### 3.1 Vấn đề khi không có (SCD Type 1)

Customer Nguyễn A đổi email từ `old@gmail.com` → `new@gmail.com` ngày 15/05/2026:
- **SCD Type 1 (overwrite):** UPDATE dim.Customer SET Email='new@gmail.com'
  → Báo cáo 1/01/2026 (đơn của Nguyễn A) hiển thị email mới → **SAI** (lúc đó email là old)

### 3.2 SCD Type 2 — giữ lịch sử

```sql
CREATE TABLE dim.Customer (
    CustomerKey   INT IDENTITY PRIMARY KEY,     -- surrogate (1 customer = N rows)
    CustomerId    BIGINT NOT NULL,              -- natural key
    FullName      NVARCHAR(200),
    Email         NVARCHAR(200),
    City          NVARCHAR(100),
    
    -- ⭐ SCD Type 2 columns
    ValidFrom     DATETIME2 NOT NULL,
    ValidTo       DATETIME2 NULL,               -- NULL = currently valid
    IsCurrent     BIT NOT NULL,
    Version       INT NOT NULL,
    RowHash       BINARY(32),                   -- SHA256 of tracked cols
);
CREATE UNIQUE INDEX UX_Customer_CurrentVersion
    ON dim.Customer(CustomerId) WHERE IsCurrent = 1;
```

**Sau khi đổi email:**

| CustomerKey | CustomerId | Email | ValidFrom | ValidTo | IsCurrent | Version |
|---|---|---|---|---|---|---|
| 42 | 1001 | old@gmail.com | 2026-01-01 | **2026-05-15** | 0 | 1 |
| 187 | 1001 | new@gmail.com | 2026-05-15 | NULL | **1** | 2 |

**Fact table reference `CustomerKey`** (surrogate), không phải `CustomerId`:
- Đơn ngày 1/01 có CustomerKey=42 → join ra email cũ
- Đơn ngày 20/05 có CustomerKey=187 → join ra email mới
- → Audit/report **chính xác theo thời điểm**

### 3.3 SCD2 logic trong ETL (project code)

```sql
-- Step 1: Compute hash to detect change cheaply
ALTER TABLE #StageCustomer ADD RowHash BINARY(32);
UPDATE #StageCustomer
SET RowHash = HASHBYTES('SHA2_256', CONCAT(FullName, '|', Email, '|', ISNULL(City, '')));

-- Step 2: Close current versions that changed
UPDATE t
SET    ValidTo = SYSUTCDATETIME(), IsCurrent = 0
FROM   dim.Customer t WITH (HOLDLOCK)
JOIN   #StageCustomer s ON s.CustomerId = t.CustomerId
WHERE  t.IsCurrent = 1
  AND  t.RowHash <> s.RowHash;

-- Step 3: Insert new versions (new customers AND changed ones)
INSERT INTO dim.Customer (..., ValidFrom, ValidTo, IsCurrent, Version, RowHash)
SELECT  s.CustomerId, ..., SYSUTCDATETIME(), NULL, 1,
        ISNULL((SELECT MAX(Version) FROM dim.Customer
                WHERE CustomerId = s.CustomerId), 0) + 1,
        s.RowHash
FROM    #StageCustomer s
WHERE   NOT EXISTS (
    SELECT 1 FROM dim.Customer t
    WHERE  t.CustomerId = s.CustomerId
      AND  t.IsCurrent = 1
      AND  t.RowHash = s.RowHash  -- skip if no change
);
```

### 3.4 SCD Type 3 và 6 — khi nào dùng?

- **SCD Type 3:** giữ 1 column "Previous Email" — tốt nếu chỉ cần biết version cuối + giá trị trước đó
- **SCD Type 6:** combo của 1+2+3 — phức tạp nhưng linh hoạt nhất

Project dùng Type 2 vì cân bằng giữa lưu lịch sử và simplicity.

---

<a name="data-quality"></a>
## 4. Data Quality Framework

### 4.1 Vấn đề khi không có

ETL load sai (vd duplicate orders, FK violation) → dashboard show sai → quyết định kinh doanh sai → mất tiền.

### 4.2 Test suite 11 tests, 5 categories

| Category | Test | Severity |
|---|---|---|
| **Uniqueness** | fact_no_duplicate_orderitemid | Critical |
| Uniqueness | dim_customer_one_current_per_id | Critical |
| **Integrity** | fact_customerkey_resolves | Critical |
| Integrity | fact_productkey_resolves | Critical |
| Integrity | fact_datekey_resolves | Critical |
| **Freshness** | fact_freshness_24h | Warning |
| Freshness | gold_freshness_2h | Warning |
| **Completeness** | fact_row_count_nonzero | Critical |
| Completeness | dim_customer_count_nonzero | Critical |
| **Business** | fact_no_negative_revenue | Critical |
| Business | fact_no_zero_quantity | Critical |
| Business | fact_unitprice_lineTotal_consistency | Warning |

### 4.3 Workflow

```
ETL job runs (every 5 min)
       ↓
Data quality job runs (offset 2 min, every 15 min)
       ↓
Each test: query OLAP → compare → persist to dq.TestResults
       ↓
If CRITICAL test failed → push SignalR alert `dq-alert`
       ↓
Frontend dashboard shows red banner
```

### 4.4 So sánh với industry tools

| Tool | Pros | Cons |
|---|---|---|
| **Great Expectations** (Python) | 100+ expectation types, comprehensive | Heavy, needs Python runtime |
| **dbt tests** | Built into dbt workflow | Requires dbt adoption |
| **Soda Core** | YAML config, multiple sources | Newer, smaller community |
| **Custom (project này)** | Lightweight, fully integrated | Limited expectation library |

Project dùng custom approach vì:
- Demo size (12 tests đủ chứng minh pattern)
- No Python dependency
- Tightly integrated với existing .NET stack

→ Phỏng vấn nói: *"Em implement custom DQ framework cho demo. Production sẽ migrate sang Great Expectations hoặc dbt tests."*

---

<a name="otel"></a>
## 5. OpenTelemetry Distributed Tracing

### 5.1 Vấn đề khi không có

Request từ frontend đi qua:
- ASP.NET Core middleware (auth, validation)
- Service layer (OrderService)
- EF Core (sinh SQL)
- SQL Server (query execution)
- Hangfire enqueue
- ETL pipeline async
- SignalR push

Khi có lỗi/chậm — **không biết bottleneck ở đâu**. Serilog logs riêng lẻ, không correlate.

### 5.2 Distributed Tracing với OTel

```
Request POST /api/orders trace tree trong Jaeger:

[HTTP POST /api/orders]                          250ms
├── [JWT Bearer validate]                          5ms
├── [FluentValidation]                            10ms
├── [OrderService.CreateAsync]                   200ms
│   ├── [SqlClient: SELECT Products WHERE Id IN] 50ms
│   ├── [SqlClient: INSERT Orders]               80ms
│   └── [SqlClient: INSERT OrderItems × N]       50ms
└── [SignalR: notify clients]                    20ms
```

→ Nhìn 1 view biết **đâu chậm nhất**, có **N+1 query** không, **lock contention** ở đâu.

### 5.3 ETL Pipeline tracing

Custom ActivitySource trong ETL:

```csharp
private static readonly ActivitySource ActivitySource = new("ECommerPipeline.Etl", "1.0.0");

public async Task RunAsync(CancellationToken ct)
{
    using var rootActivity = ActivitySource.StartActivity("etl.sales.run");
    rootActivity?.SetTag("pipeline.name", PipelineName);
    
    using (var dimActivity = ActivitySource.StartActivity("etl.dimensions.upsert"))
    {
        await UpsertDimensionsAsync(conn, ct);
    }
    
    while (...) {
        using var batchActivity = ActivitySource.StartActivity("etl.batch");
        batchActivity?.SetTag("rows.extracted", rows.Count);
        // ... batch processing
    }
    
    using (var goldActivity = ActivitySource.StartActivity("etl.gold.refresh")) { ... }
    
    rootActivity?.SetTag("rows.total", totalProcessed);
    rootActivity?.SetTag("duration.ms", sw.ElapsedMilliseconds);
}
```

→ Trong Jaeger thấy:
```
[etl.sales.run]                       45s
├── [etl.dimensions.upsert]            2s
├── [etl.batch #1]                   8s    rows.extracted=5000
├── [etl.batch #2]                   7s    rows.extracted=5000
├── [etl.batch #3]                   8s    rows.extracted=5000
├── [etl.batch #4]                   3s    rows.extracted=200
└── [etl.gold.refresh]               12s
```

### 5.4 Setup

**Docker Compose:**
```yaml
jaeger:
  image: jaegertracing/all-in-one:1.62
  environment:
    COLLECTOR_OTLP_ENABLED: "true"
  ports:
    - "16686:16686"  # UI
    - "4317:4317"    # OTLP gRPC
```

**API ENV:**
```yaml
Otel__Endpoint: "http://jaeger:4317"
```

**Verify:**
1. `docker compose up -d`
2. Mở **http://localhost:16686**
3. Service: `ECommerPipeline.Api`
4. Find traces → click any → see span tree

### 5.5 Production observability stack

| Layer | Open-source | Commercial |
|---|---|---|
| Traces | Jaeger / Tempo | Datadog APM, New Relic |
| Metrics | Prometheus + Grafana | Datadog Metrics |
| Logs | Loki / Seq | Splunk, Datadog Logs |
| All-in-one | Grafana stack | Datadog, Dynatrace |

→ Project dùng Jaeger demo. Sang production sẽ thêm Prometheus metrics + Grafana dashboard.

---

<a name="enterprise-comparison"></a>
## 6. So sánh project với enterprise reality

### 6.1 Project ECommerPipeline vs Tier 2 doanh nghiệp (mid-size)

| Pattern | Project | Tier 2 enterprise |
|---|---|---|
| **OLTP/OLAP split** | ✅ SQL Server x2 | ✅ Postgres → Snowflake |
| **Medallion (Bronze/Silver/Gold)** | ✅ 3 schemas in OLAP | ✅ S3 buckets / Delta Lake layers |
| **SCD Type 2** | ✅ HASHBYTES + version | ✅ dbt snapshot macros |
| **Incremental ETL** | ✅ Watermark | ⚠️ CDC (Debezium → Kafka) |
| **Background jobs** | ✅ Hangfire | ✅ Airflow / Dagster DAGs |
| **Data quality** | ✅ 11 custom tests | ✅ Great Expectations / dbt tests |
| **Distributed tracing** | ✅ OpenTelemetry → Jaeger | ✅ OpenTelemetry → Tempo/Datadog |
| **Real-time push** | ✅ SignalR | ✅ Kafka events |
| **Event-driven architecture** | ❌ polling 5 min | ✅ Kafka topics |
| **Schema registry** | ❌ | ✅ Confluent Schema Registry |
| **Data lineage** | ❌ | ✅ DataHub / Marquez |
| **Partitioning** | ❌ | ✅ By month/year |
| **Multi-tenant** | ❌ | ⚠️ Often (depends) |

### 6.2 Gap còn lại (production-grade)

Để full enterprise, còn cần:
1. **Kafka + CDC** thay polling (real-time)
2. **Partitioning** fact tables theo tháng
3. **Materialized views** auto-refresh
4. **Audit logs** (ai sửa gì khi nào)
5. **Multi-tenancy** (tenant_id partition)
6. **GDPR compliance** (data retention, right to erasure)
7. **Disaster recovery** (backup + replication)
8. **Cost monitoring** (storage tier, query cost)

→ Mỗi cái cần 1-2 tuần work. Project demo dừng ở "Tier 1+" (startup-grade with enterprise pattern foundation).

---

## 7. Cách nói trong phỏng vấn

### Q: "Em build data pipeline thế nào?"

> "Em apply Medallion Architecture: Bronze layer lưu raw, Silver có star schema với fact (Columnstore) + dimensions, Gold pre-aggregate cho dashboard. ETL Hangfire chạy mỗi 5 phút theo watermark pattern.
>
> Dimensions dùng SCD Type 2 — em tính SHA256 hash để detect change cheaply, close current version + insert new version khi có thay đổi. Fact reference surrogate keys nên historical reports show đúng customer state lúc order.
>
> Có 11 data quality tests chia 5 categories (Uniqueness/Integrity/Freshness/Completeness/Business). Chạy sau ETL, fail critical → SignalR alert dashboard.
>
> Em wire OpenTelemetry trace mọi span: HTTP request → EF Core → SQL Server → ETL. Export vào Jaeger qua OTLP để visualize bottleneck."

### Q: "Vì sao không dùng Kafka + CDC?"

> "Em biết Kafka + CDC là pattern enterprise chuẩn cho real-time ETL. Em chọn polling + watermark cho demo vì:
> 1. Single-server architecture (Kafka cần cluster)
> 2. SQL Server Developer edition không có Always On CDC stable
> 3. Watermark đủ cho 5-min latency
>
> Production em sẽ migrate sang Debezium → Kafka → consumer service. Em đã document trong roadmap V2."

### Q: "Sao em không dùng dbt?"

> "dbt là tool excellent cho SQL transformation với version control + tests. Project em dùng raw SQL trong ETL service vì:
> 1. Demo .NET stack, không muốn mix Python runtime
> 2. dbt yêu cầu separate orchestration (Airflow)
> 3. Scope nhỏ — 3 Gold tables, dbt overhead không justify
>
> Khi scale lên 50+ transformation, em sẽ adopt dbt + Dagster."

---

## 📊 Bonus: Performance numbers

| Layer | Query | Latency |
|---|---|---|
| Silver fact (300k rows, no compress) | SUM by Category | ~13,000 ms ⚠️ |
| Silver fact (compressed Columnstore) | SUM by Category | ~90 ms ✅ |
| **Gold pre-aggregated** | SUM by Category | **~5-10 ms** 🚀 |
| Compression ratio | Silver fact | ~10× (50MB → 5MB) |
| ETL throughput | Bronze + Silver + Gold | ~5000 rows/s |

→ Gold layer = **10× speedup** so với Silver compressed = **1000× speedup** so với delta-store.

---

## 🎓 Học sâu hơn

**Sách:**
- *"The Data Warehouse Toolkit"* — Ralph Kimball (bible của dimensional modeling)
- *"Designing Data-Intensive Applications"* — Martin Kleppmann

**Tài liệu online:**
- [Databricks Medallion Architecture](https://www.databricks.com/glossary/medallion-architecture)
- [dbt SCD Type 2](https://docs.getdbt.com/docs/build/snapshots)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Great Expectations docs](https://docs.greatexpectations.io/)
