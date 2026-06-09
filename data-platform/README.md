# Data Platform — Modern Data Stack pipeline (Python · dbt · Airflow · DuckDB→BigQuery)

A **Data Engineering** layer built on top of the same e-commerce OLTP database as
the main app, re-implementing the analytics pipeline with the **industry-standard
DE toolchain** instead of .NET/Hangfire.

```
SQL Server OLTP  ──Python extract──►  parquet landing (raw)  ──load──►  DuckDB / BigQuery
   (Customers,                          = Bronze                          │
    Products,                                                             ▼
    Orders,                                                          dbt models
    OrderItems)                                          staging → marts (Silver → Gold)
                                                         + SCD Type 2 snapshots
                                                         + data-quality tests
                                                                          │
                                                       Apache Airflow orchestrates the whole DAG
```

> **Why this exists:** the .NET pipeline already proves the *concepts* (Medallion,
> SCD Type 2, incremental ETL, data quality). This folder proves the same concepts
> in the toolchain that Data Engineer roles actually hire for: **Python, dbt,
> Airflow, a cloud warehouse**.

---

## Architecture decisions (so you can defend them)

| Decision | Why |
|---|---|
| **Extract to parquet first**, then load | A file landing zone = replay without re-hitting OLTP. Same role as the Bronze layer. In production this is S3/GCS. |
| **DuckDB for dev, BigQuery for prod** (dbt targets) | Runs locally today with zero cloud setup; swap to BigQuery by changing a dbt *target*, not code. |
| **dbt for transforms** | Version-controlled SQL, built-in tests, `snapshot` gives SCD Type 2 for free. The DE standard. |
| **Airflow for orchestration** | Replaces Hangfire with the tool DE teams use; gives DAGs, retries, backfills, scheduling. |
| **Full extract on Day 1** | Correct and simple. Incremental/watermark is a deliberate Day-2 upgrade (already done in the .NET ETL). |

---

## Layout

```
data-platform/
├── extract/
│   └── extract_oltp.py     # E + landing: SQL Server → parquet → DuckDB raw schema
├── data/raw/               # parquet landing files (gitignored, regenerated)
├── transform/              # dbt project  (Day 2)
├── orchestration/          # Airflow DAG + docker-compose  (Day 3)
├── requirements.txt
├── .env.example            # copy to .env
└── warehouse.duckdb        # local warehouse (gitignored, regenerated)
```

---

## Prerequisites

1. **Python 3.10+**
2. **The root app's SQL Server running** (the extract reads from it):
   ```bash
   # from the repo root
   docker compose up -d sql
   ```
   Make sure the OLTP DB has data (run the app once so it seeds Customers/Products/Orders).
3. **Microsoft ODBC Driver 18 for SQL Server** (for `pyodbc`):
   - Windows: download "ODBC Driver 18 for SQL Server" from Microsoft and install.
   - Check installed drivers in Python: `python -c "import pyodbc; print(pyodbc.drivers())"`
   - If you have Driver 17 instead, set `OLTP_ODBC_DRIVER=ODBC Driver 17 for SQL Server` in `.env`.

---

## Run — Day 1 (Extract)

```bash
cd data-platform

# 1. create + activate a virtual env
python -m venv .venv
.venv\Scripts\activate          # Windows PowerShell
# source .venv/bin/activate     # macOS/Linux

# 2. install deps
pip install -r requirements.txt

# 3. configure
copy .env.example .env          # Windows  (cp on macOS/Linux)
#   edit .env only if your SQL Server host/password differ

# 4. run the extract
python extract/extract_oltp.py
```

Expected output:
```
  ✓ customers       5,000 rows  →  raw.customers  +  customers.parquet
  ✓ products        1,000 rows  →  raw.products   +  products.parquet
  ✓ orders        100,000 rows  →  raw.orders     +  orders.parquet
  ✓ order_items   ~300,000 rows →  raw.order_items + order_items.parquet
✓ Extract complete.
```

**Verify the data landed** (optional, nice to see):
```bash
python -c "import duckdb; c=duckdb.connect('warehouse.duckdb'); print(c.sql('SELECT table_name, estimated_size FROM duckdb_tables()'))"
```

---

## Roadmap

- [x] **Day 1 — Extract / source**: two interchangeable sources landing into the
      same DuckDB `raw` schema (+ parquet):
      - `extract/extract_oltp.py` — reads the **real SQL Server OLTP** via pyodbc
        (the "production" extract; needs the Docker SQL container up).
      - `extract/generate_raw.py` — **synthetic generator** for local dev, no Docker.
- [ ] **Day 2 — Transform (dbt)**: `staging` (clean) → `marts` (star schema = Silver) →
      Gold aggregates; SCD Type 2 via `dbt snapshot`; data-quality tests.
- [ ] **Day 3 — Orchestrate**: one pipeline `source → dbt run → dbt test`.
- [ ] **Later (P1/P2)**: BigQuery prod target · incremental models (watermark) ·
      Great Expectations · CDC (Debezium/Kafka).

---

## ⏸️ STATUS — paused (resume after SSD/RAM upgrade)

**Where we stopped:** Day-1 scaffolding is done and committed on branch
`feat/data-platform-p0`. The Python env is set up (`.venv`) with the
**pandas/numpy-free** extract stack (`pyodbc` + `duckdb`).

**Why paused:** this machine doesn't have enough RAM to run the Docker stack
(SQL Server + services) *and* Python at the same time — every attempt hit
"paging file too small" / OpenBLAS / Docker engine crashes. Decision: pause until
the hardware upgrade, then resume.

**When you come back (resume checklist):**
1. `git checkout feat/data-platform-p0`
2. **Get data into `raw`** — pick ONE:
   - Real OLTP: `docker compose up -d sql` (wait healthy), then
     `cd data-platform && .venv\Scripts\python extract\extract_oltp.py`
   - OR no Docker: `cd data-platform && .venv\Scripts\python extract\generate_raw.py`
   - (set `PYTHONUTF8=1` so Vietnamese text prints on Windows console)
3. Then build **Day 2 (dbt)** on schema `raw`.

**Toolchain note for Day 3:** Airflow needs Docker/WSL (heavy). On this hardware
prefer **Dagster** (`pip install dagster dagster-dbt`, runs native via
`dagster dev`, light) — same DE-orchestrator story, no Docker. Switch back to
Airflow later if/when the box can handle it.

