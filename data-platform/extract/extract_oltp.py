"""
extract_oltp.py — the "E" (Extract) and "L" (Load to landing) of the pipeline.

WHAT IT DOES
------------
1. Connects to the SQL Server OLTP database (ECommerPipeline_Oltp).
2. Pulls the 4 source tables we need for analytics:
       Customers, Products, Orders, OrderItems
3. Writes each as a parquet file into data/raw/   <-- this is the "landing zone"
       (conceptually the BRONZE layer: raw, untransformed, 1:1 copy of source)
4. Loads those parquet files into a local DuckDB warehouse, schema `raw`.
       dbt will then build staging -> marts (Silver/Gold) on top of `raw`.

WHY THIS SHAPE (interview talking points)
-----------------------------------------
- We extract to parquet FIRST (a file landing zone) before loading. In a real
  modern stack this landing zone is object storage (S3/GCS). It lets us replay
  transformations without re-hitting the source OLTP — same idea as the Bronze
  layer in the .NET pipeline.
- We add an `_extracted_at` column = the ingestion timestamp. That is metadata
  the source doesn't have; it is the first tiny "transformation" and is useful
  for freshness checks / debugging.
- Day-1 does a FULL extract (simple, correct). The watermark/incremental
  optimization (only pull new OrderItems) is a deliberate Day-2 upgrade — we
  already implemented that pattern in the .NET ETL, so porting it here is a
  natural next step, not an accident.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import duckdb
import pandas as pd
from dotenv import load_dotenv
from sqlalchemy import create_engine, text
from sqlalchemy.engine import URL

# ──────────────────────────────────────────────────────────────────────────
# Paths — everything is relative to this file so it runs from anywhere.
# ──────────────────────────────────────────────────────────────────────────
HERE = Path(__file__).resolve().parent          # .../data-platform/extract
PLATFORM_ROOT = HERE.parent                      # .../data-platform
RAW_DIR = PLATFORM_ROOT / "data" / "raw"         # parquet landing zone

# ──────────────────────────────────────────────────────────────────────────
# The tables we extract. Key = the table name we'll use in the warehouse,
# value = the SQL that reads it from OLTP. We select only the columns we need
# for analytics (we intentionally DROP PasswordHash, Role, etc. — analytics has
# no business seeing auth secrets).
# ──────────────────────────────────────────────────────────────────────────
SOURCE_QUERIES: dict[str, str] = {
    "customers": """
        SELECT Id, FullName, Email, Phone, City, CreatedAt, UpdatedAt
        FROM   dbo.Customers
    """,
    "products": """
        SELECT Id, Sku, Name, Category, Brand, Price, StockQuantity, CreatedAt, UpdatedAt
        FROM   dbo.Products
    """,
    "orders": """
        SELECT Id, OrderNumber, CustomerId, OrderDate, Status, TotalAmount, CreatedAt, UpdatedAt
        FROM   dbo.Orders
    """,
    "order_items": """
        SELECT Id, OrderId, ProductId, Quantity, UnitPrice, LineTotal, CreatedAt, UpdatedAt
        FROM   dbo.OrderItems
    """,
}


def build_oltp_engine():
    """Create a SQLAlchemy engine pointed at the SQL Server OLTP database.

    We use URL.create() instead of a raw string so the password can contain
    special characters (ours has '@') without manual URL-escaping.
    """
    connection_url = URL.create(
        "mssql+pyodbc",
        username=os.environ["OLTP_USER"],
        password=os.environ["OLTP_PASSWORD"],
        host=os.environ["OLTP_HOST"],
        port=int(os.environ.get("OLTP_PORT", 1433)),
        database=os.environ["OLTP_DB"],
        query={
            "driver": os.environ.get("OLTP_ODBC_DRIVER", "ODBC Driver 18 for SQL Server"),
            # Dev SQL Server uses a self-signed cert; trust it and don't force encryption.
            "TrustServerCertificate": "yes",
            "Encrypt": "no",
        },
    )
    return create_engine(connection_url)


def extract_table(engine, name: str, query: str) -> pd.DataFrame:
    """Run one extract query and return a DataFrame, stamped with ingestion time."""
    with engine.connect() as conn:
        df = pd.read_sql(text(query), conn)
    # The single source-agnostic piece of metadata we add at ingestion time.
    df["_extracted_at"] = pd.Timestamp.utcnow()
    return df


def main() -> int:
    # Load credentials from data-platform/.env (falls back to real env vars).
    load_dotenv(PLATFORM_ROOT / ".env")

    RAW_DIR.mkdir(parents=True, exist_ok=True)
    duckdb_path = PLATFORM_ROOT / os.environ.get("DUCKDB_PATH", "warehouse.duckdb")

    print(f"→ Source : SQL Server {os.environ.get('OLTP_HOST')}:{os.environ.get('OLTP_PORT')}"
          f"/{os.environ.get('OLTP_DB')}")
    print(f"→ Landing: {RAW_DIR}")
    print(f"→ Warehouse: {duckdb_path}\n")

    try:
        engine = build_oltp_engine()
    except KeyError as e:
        print(f"✗ Missing config {e}. Did you copy .env.example to .env?", file=sys.stderr)
        return 1

    # Open the local DuckDB warehouse and make sure the `raw` schema exists.
    con = duckdb.connect(str(duckdb_path))
    con.execute("CREATE SCHEMA IF NOT EXISTS raw;")

    total_rows = 0
    for name, query in SOURCE_QUERIES.items():
        try:
            df = extract_table(engine, name, query)
        except Exception as e:  # noqa: BLE001 — surface any connection/query error clearly
            print(f"✗ Failed extracting '{name}': {e}", file=sys.stderr)
            print("  Tip: is the Docker SQL container up?  docker compose up -d sql", file=sys.stderr)
            return 1

        # 1) Write the raw parquet landing file (Bronze-style raw copy).
        parquet_path = RAW_DIR / f"{name}.parquet"
        df.to_parquet(parquet_path, index=False)

        # 2) (Re)load it into the DuckDB `raw` schema so dbt can read it.
        #    Full refresh on Day 1: drop + recreate from the parquet we just wrote.
        con.execute(f"DROP TABLE IF EXISTS raw.{name};")
        con.execute(f"CREATE TABLE raw.{name} AS SELECT * FROM read_parquet('{parquet_path.as_posix()}');")

        rows = len(df)
        total_rows += rows
        print(f"  ✓ {name:<12} {rows:>8,} rows  →  raw.{name}  +  {parquet_path.name}")

    con.close()
    print(f"\n✓ Extract complete. {total_rows:,} rows landed across {len(SOURCE_QUERIES)} tables.")
    print("  Next: build the dbt models on top of schema `raw`.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
