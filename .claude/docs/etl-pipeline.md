# ETL Pipeline

File: `src/ECommerPipeline.Infrastructure/Etl/SalesEtlPipeline.cs`. Triggered by `EtlJob` (Hangfire, Polly retry) on cron `Jobs:EtlCron` (default every 5 min) or manually via `POST /api/admin/trigger-etl`.

## Medallion layers (OLAP database)

- **Bronze** (`bronze.OrderItem_Raw`): 1:1 raw copy from OLTP OrderItems. Immutable landing zone for replay/audit. `BronzeKey` IDENTITY + `IngestedAt`/`SourceSystem` defaults.
- **Silver** (`fact.SalesOrderItem` + `dim.*`): cleaned star schema. Fact is Clustered Columnstore, references dimension **surrogate keys**.
- **Gold** (`gold.DailySalesByCategory`, `gold.MonthlyTopProducts`, `gold.CustomerLifetimeValue`): pre-aggregated. Reports query these (~5-10ms).

## RunAsync flow

```
GetWatermarkAsync()                  → last processed OrderItemId from etl.Watermark
UpsertDimensionsAsync()              → SCD2 upsert dim.Customer, dim.Product; ensure dim.Date
LoadKeyLookupsAsync()                → CustomerId→CustomerKey, ProductId→ProductKey (WHERE IsCurrent=1)
loop batches of 5000 (Id > watermark):
    build Bronze DataTable (8 cols) + Silver fact DataTable (9 cols, with surrogate keys + DateKey)
    BEGIN TRAN:
        BulkLoadAsync(bronze)        → stage temp table → INSERT WHERE NOT EXISTS (dedup)
        BulkLoadAsync(fact)          → direct SqlBulkCopy
        UpdateWatermarkAsync(maxId)
    COMMIT
RefreshGoldLayerAsync()              → TRUNCATE + INSERT the 3 gold tables (window funcs for top products)
NotifyEtlCompletedAsync()            → SignalR "etl-completed"
```

`DateKey` = `int.Parse(orderDate.ToString("yyyyMMdd"))` (e.g. 20260519). Chronologically sortable integer PK for `dim.Date`.

## Watermark pattern

`etl.Watermark(PipelineName, LastProcessedRowId, ...)`. Only `WHERE Id > watermark` is extracted → incremental, idempotent, resumable. Watermark advances **inside the same transaction** as the bronze+fact load, so a rollback leaves it unchanged (safe retry). Limitation: does NOT capture UPDATE/DELETE (would need CDC).

## SCD Type 2 (dim.Customer, dim.Product)

Columns: `ValidFrom`, `ValidTo` (NULL = current), `IsCurrent`, `Version`, `RowHash BINARY(32)`. Logic in `BulkUpsertCustomersAsync`/`BulkUpsertProductsAsync`:
1. Stage rows (RowHash column declared in CREATE TABLE — NOT added via ALTER), compute `RowHash = HASHBYTES('SHA2_256', concat of tracked cols)`.
2. Close changed current versions: `UPDATE ... SET ValidTo=now, IsCurrent=0 WHERE IsCurrent=1 AND RowHash <> staged`.
3. Insert new versions (new + changed) with `Version = max+1`, skipping unchanged (`WHERE NOT EXISTS matching current RowHash`).

Filtered unique index `UX_*_CurrentVersion ... WHERE IsCurrent=1` enforces one current row per natural key. Fact references the surrogate key current at load time → historical reports reflect the dimension state at order time.

## Concurrency safety

- `EtlJob` has `[DisableConcurrentExecution(600)]` (Hangfire distributed lock).
- Dimension MERGE/UPDATE use `WITH (HOLDLOCK)` for atomic match-then-write.

## SQL gotchas encountered (do not reintroduce)

- **ALTER-then-use in same batch:** SQL Server compiles the whole batch first → "Invalid column name". Fix: declare the column in `CREATE TABLE`.
- **`SELECT TOP 0 * INTO #stage`:** copies NOT NULL but drops DEFAULTs → bulk copy fails on IDENTITY/default columns. Fix: `CREATE TABLE #stage (...)` explicitly with only the loaded columns.
- **Dapper transaction:** `new CommandDefinition(sql, parameters, transaction, ...)` — 2nd positional is `parameters`. Pass tx as `transaction: tx` (named) or in the correct position.

## Sibling jobs

- `CompressColumnstoreJob` (`Jobs:CompressCron`, default 2 AM): `ALTER INDEX ... REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON)`. Needed because low-volume data stays in delta store (slow) until compressed.
- `DataQualityJob` (`Jobs:DataQualityCron`, default every 15 min): 11 checks across Uniqueness/Integrity/Freshness/Completeness/Business → `dq.TestResults`. Critical failures push SignalR `dq-alert`.
