"""
extract_oltp.py — the "E" (Extract) and "L" (Load to landing) of the pipeline.

WHAT IT DOES
------------
1. Connects to the SQL Server OLTP database (ECommerPipeline_Oltp) via pyodbc.
2. Pulls the 4 source tables we need for analytics:
       Customers, Products, Orders, OrderItems
3. Loads each into a local DuckDB warehouse under schema `raw`, with an
   explicit, typed schema (we control the column types — defendable & predictable).
4. Exports each raw table to parquet in data/raw/  <-- the "landing zone"
       (conceptually the BRONZE layer: raw, untransformed, 1:1 copy of source).
   dbt will then build staging -> marts (Silver/Gold) on top of `raw`.

WHY pyodbc + duckdb (no pandas)?
--------------------------------
We deliberately avoid pandas/numpy here. It keeps the dependency set tiny
(pyodbc + duckdb) and sidesteps numpy/OpenBLAS issues, and — more importantly —
forces us to declare the warehouse schema explicitly instead of relying on type
inference. DuckDB writes the parquet itself via COPY.

INTERVIEW TALKING POINTS
------------------------
- We land to parquet FIRST (a file landing zone) so transformations can be
  replayed without re-hitting OLTP — same idea as the Bronze layer.
- We add an `_extracted_at` column = ingestion timestamp (metadata the source
  lacks; useful for freshness checks).
- Day-1 does a FULL extract (simple, correct). The watermark/incremental
  optimization (only pull new OrderItems) is a deliberate Day-2 upgrade — the
  pattern is already implemented in the .NET ETL, so porting it is natural.
"""

from __future__ import annotations

import os
import sys
from datetime import datetime, timezone
from pathlib import Path

import duckdb
import pyodbc
from dotenv import load_dotenv

# ──────────────────────────────────────────────────────────────────────────
HERE = Path(__file__).resolve().parent          # .../data-platform/extract
PLATFORM_ROOT = HERE.parent                      # .../data-platform
RAW_DIR = PLATFORM_ROOT / "data" / "raw"         # parquet landing zone

# ──────────────────────────────────────────────────────────────────────────
# Each source table: the SELECT against OLTP + the explicit DuckDB column DDL.
# We select only analytics-relevant columns (we DROP auth secrets like
# PasswordHash/Role on purpose). The `_extracted_at` column is appended in code.
# ──────────────────────────────────────────────────────────────────────────
TABLES: dict[str, dict] = {
    "customers": {
        "select": "SELECT Id, FullName, Email, Phone, City, CreatedAt, UpdatedAt FROM dbo.Customers",
        "ddl": """
            Id BIGINT, FullName VARCHAR, Email VARCHAR, Phone VARCHAR, City VARCHAR,
            CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
        """,
    },
    "products": {
        "select": "SELECT Id, Sku, Name, Category, Brand, Price, StockQuantity, CreatedAt, UpdatedAt FROM dbo.Products",
        "ddl": """
            Id BIGINT, Sku VARCHAR, Name VARCHAR, Category VARCHAR, Brand VARCHAR,
            Price DECIMAL(18,2), StockQuantity INTEGER,
            CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
        """,
    },
    "orders": {
        "select": "SELECT Id, OrderNumber, CustomerId, OrderDate, Status, TotalAmount, CreatedAt, UpdatedAt FROM dbo.Orders",
        "ddl": """
            Id BIGINT, OrderNumber VARCHAR, CustomerId BIGINT, OrderDate TIMESTAMP,
            Status INTEGER, TotalAmount DECIMAL(18,2),
            CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
        """,
    },
    "order_items": {
        "select": "SELECT Id, OrderId, ProductId, Quantity, UnitPrice, LineTotal, CreatedAt, UpdatedAt FROM dbo.OrderItems",
        "ddl": """
            Id BIGINT, OrderId BIGINT, ProductId BIGINT, Quantity INTEGER,
            UnitPrice DECIMAL(18,2), LineTotal DECIMAL(18,2),
            CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
        """,
    },
}


def build_odbc_conn_str() -> str:
    """Assemble the pyodbc connection string from environment variables."""
    return (
        f"DRIVER={{{os.environ.get('OLTP_ODBC_DRIVER', 'ODBC Driver 18 for SQL Server')}}};"
        f"SERVER={os.environ['OLTP_HOST']},{os.environ.get('OLTP_PORT', '1433')};"
        f"DATABASE={os.environ['OLTP_DB']};"
        f"UID={os.environ['OLTP_USER']};PWD={os.environ['OLTP_PASSWORD']};"
        # Dev SQL Server uses a self-signed cert; trust it, don't force encryption.
        "TrustServerCertificate=yes;Encrypt=no;"
    )


def column_count(ddl: str) -> int:
    """How many columns the DDL declares — used to build the INSERT placeholders."""
    return len([c for c in ddl.split(",") if c.strip()])


def main() -> int:
    load_dotenv(PLATFORM_ROOT / ".env")
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    duckdb_path = PLATFORM_ROOT / os.environ.get("DUCKDB_PATH", "warehouse.duckdb")

    print(f"-> Source   : SQL Server {os.environ.get('OLTP_HOST')}:{os.environ.get('OLTP_PORT')}"
          f"/{os.environ.get('OLTP_DB')}")
    print(f"-> Landing  : {RAW_DIR}")
    print(f"-> Warehouse: {duckdb_path}\n")

    try:
        src = pyodbc.connect(build_odbc_conn_str(), timeout=60)
    except KeyError as e:
        print(f"x Missing config {e}. Did you copy .env.example to .env?", file=sys.stderr)
        return 1
    except pyodbc.Error as e:
        print(f"x Cannot connect to OLTP: {e}", file=sys.stderr)
        print("  Tip: is the Docker SQL container up?  docker compose up -d sql", file=sys.stderr)
        return 1

    con = duckdb.connect(str(duckdb_path))
    con.execute("CREATE SCHEMA IF NOT EXISTS raw;")

    extracted_at = datetime.now(timezone.utc)
    total_rows = 0

    for name, spec in TABLES.items():
        # 1) Read all rows from OLTP, append the ingestion timestamp to each.
        cur = src.cursor()
        cur.execute(spec["select"])
        rows = [tuple(r) + (extracted_at,) for r in cur.fetchall()]
        cur.close()

        # 2) (Re)create the typed raw table in DuckDB and insert.
        con.execute(f"DROP TABLE IF EXISTS raw.{name};")
        con.execute(f"CREATE TABLE raw.{name} ({spec['ddl']});")
        if rows:
            placeholders = ", ".join(["?"] * column_count(spec["ddl"]))
            con.executemany(f"INSERT INTO raw.{name} VALUES ({placeholders});", rows)

        # 3) Export the raw table to a parquet landing file (Bronze-style copy).
        parquet_path = RAW_DIR / f"{name}.parquet"
        con.execute(f"COPY raw.{name} TO '{parquet_path.as_posix()}' (FORMAT PARQUET);")

        total_rows += len(rows)
        print(f"  ok {name:<12} {len(rows):>8,} rows  ->  raw.{name}  +  {parquet_path.name}")

    con.close()
    src.close()
    print(f"\nOK Extract complete. {total_rows:,} rows landed across {len(TABLES)} tables.")
    print("   Next: build the dbt models on top of schema `raw`.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
