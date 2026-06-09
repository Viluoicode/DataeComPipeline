"""
generate_raw.py — local-dev data source (no Docker / no SQL Server needed).

WHY THIS EXISTS
---------------
extract_oltp.py reads from the *real* SQL Server OLTP database — that's the
"production" extract and proves we can pull from a live source over ODBC.
But running SQL Server (in Docker) alongside Python is too heavy for a
memory-constrained machine. So for LOCAL DEVELOPMENT we generate synthetic
e-commerce data that lands in the EXACT SAME `raw` schema (and parquet files).

Because the output schema is identical, every downstream model (dbt) and the
orchestrator (Dagster) behave the same whether the data came from SQL Server or
from this generator. Swapping the source is a one-line decision, not a rewrite.

This is a real industry pattern: seed/mock data for dev, real connectors for prod.

USAGE
-----
    python extract/generate_raw.py
    # volumes overridable via env: GEN_CUSTOMERS, GEN_PRODUCTS, GEN_ORDERS

Pure standard library + duckdb. No pandas/numpy (keeps it tiny and avoids the
numpy/OpenBLAS issues on this box).
"""

from __future__ import annotations

import os
import random
from datetime import datetime, timedelta, timezone
from pathlib import Path

import duckdb

HERE = Path(__file__).resolve().parent
PLATFORM_ROOT = HERE.parent
RAW_DIR = PLATFORM_ROOT / "data" / "raw"

# Deterministic output so re-runs are reproducible (nice for debugging/tests).
random.seed(42)

# ── Volumes (match the real OLTP seed sizes so dev data is comparable) ──────
N_CUSTOMERS = int(os.environ.get("GEN_CUSTOMERS", 500))
N_PRODUCTS = int(os.environ.get("GEN_PRODUCTS", 100))
N_ORDERS = int(os.environ.get("GEN_ORDERS", 5000))

# ── Same typed schema as extract_oltp.py (keep these in sync) ───────────────
DDL: dict[str, str] = {
    "customers": """
        Id BIGINT, FullName VARCHAR, Email VARCHAR, Phone VARCHAR, City VARCHAR,
        CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
    """,
    "products": """
        Id BIGINT, Sku VARCHAR, Name VARCHAR, Category VARCHAR, Brand VARCHAR,
        Price DECIMAL(18,2), StockQuantity INTEGER,
        CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
    """,
    "orders": """
        Id BIGINT, OrderNumber VARCHAR, CustomerId BIGINT, OrderDate TIMESTAMP,
        Status INTEGER, TotalAmount DECIMAL(18,2),
        CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
    """,
    "order_items": """
        Id BIGINT, OrderId BIGINT, ProductId BIGINT, Quantity INTEGER,
        UnitPrice DECIMAL(18,2), LineTotal DECIMAL(18,2),
        CreatedAt TIMESTAMP, UpdatedAt TIMESTAMP, _extracted_at TIMESTAMP
    """,
}

# ── Small curated pools for realistic-ish e-commerce data (VN flavored) ─────
LAST = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Vũ", "Đặng", "Bùi", "Đỗ", "Hồ"]
MID = ["Văn", "Thị", "Hữu", "Đức", "Minh", "Ngọc", "Quang", "Thanh"]
FIRST = ["An", "Bình", "Châu", "Dũng", "Hà", "Khánh", "Linh", "Nam", "Phúc", "Quân",
         "Sơn", "Trang", "Uyên", "Vy", "Yến"]
CITIES = ["Hà Nội", "TP.HCM", "Đà Nẵng", "Hải Phòng", "Cần Thơ", "Biên Hòa",
          "Huế", "Nha Trang", "Vũng Tàu", "Đồng Nai"]
CATEGORIES = {
    "Electronics": ["Sony", "Samsung", "Apple", "Xiaomi", "LG"],
    "Fashion": ["Uniqlo", "Zara", "H&M", "Adidas", "Nike"],
    "Home": ["IKEA", "Lock&Lock", "Sunhouse", "Elmich"],
    "Books": ["NXB Trẻ", "NXB Kim Đồng", "Penguin", "O'Reilly"],
    "Beauty": ["L'Oréal", "Innisfree", "The Ordinary", "Cocoon"],
}
PRODUCT_NOUNS = ["Tai nghe", "Áo thun", "Nồi chiên", "Sách", "Kem dưỡng", "Bàn phím",
                 "Giày", "Bình giữ nhiệt", "Chuột", "Balo", "Sạc dự phòng", "Quần"]

NOW = datetime.now(timezone.utc).replace(microsecond=0)


def rand_dt(days_back_max: int) -> datetime:
    """A random timestamp within the last `days_back_max` days."""
    delta = timedelta(
        days=random.randint(0, days_back_max),
        seconds=random.randint(0, 86399),
    )
    return (NOW - delta).replace(microsecond=0)


def gen_customers() -> list[tuple]:
    rows = []
    for i in range(1, N_CUSTOMERS + 1):
        name = f"{random.choice(LAST)} {random.choice(MID)} {random.choice(FIRST)}"
        email = f"user{i}@example.com"
        phone = "09" + "".join(random.choices("0123456789", k=8))
        city = random.choice(CITIES)
        created = rand_dt(730)  # up to 2 years ago
        rows.append((i, name, email, phone, city, created, created, NOW))
    return rows


def gen_products() -> list[tuple]:
    rows = []
    cats = list(CATEGORIES.keys())
    for i in range(1, N_PRODUCTS + 1):
        category = random.choice(cats)
        brand = random.choice(CATEGORIES[category])
        name = f"{random.choice(PRODUCT_NOUNS)} {brand} {random.randint(100, 999)}"
        sku = f"SKU-{i:05d}"
        price = round(random.uniform(50_000, 5_000_000), 2)  # VND
        stock = random.randint(0, 500)
        created = rand_dt(730)
        rows.append((i, sku, name, category, brand, price, stock, created, created, NOW))
    return rows


def gen_orders_and_items(products: list[tuple]) -> tuple[list[tuple], list[tuple]]:
    """Generate orders, then 1-5 line items each. TotalAmount = sum(line totals)
    so the data is internally consistent (good for later data-quality tests)."""
    orders, items = [], []
    item_id = 0
    # product price lookup: product Id -> Price (index 0 = Id, index 5 = Price)
    price_of = {p[0]: float(p[5]) for p in products}
    for oid in range(1, N_ORDERS + 1):
        customer_id = random.randint(1, N_CUSTOMERS)
        order_date = rand_dt(365)  # last year
        status = random.randint(0, 4)  # mirrors OrderStatus enum range
        n_items = random.randint(1, 5)
        total = 0.0
        for _ in range(n_items):
            item_id += 1
            pid = random.randint(1, N_PRODUCTS)
            qty = random.randint(1, 4)
            unit = round(price_of[pid], 2)
            line = round(unit * qty, 2)
            total += line
            items.append((item_id, oid, pid, qty, unit, line,
                          order_date, order_date, NOW))
        order_no = f"ORD-{order_date:%Y%m%d}-{oid:06d}"
        orders.append((oid, order_no, customer_id, order_date, status,
                       round(total, 2), order_date, order_date, NOW))
    return orders, items


def load(con, name: str, rows: list[tuple]) -> None:
    con.execute(f"DROP TABLE IF EXISTS raw.{name};")
    con.execute(f"CREATE TABLE raw.{name} ({DDL[name]});")
    if rows:
        placeholders = ", ".join(["?"] * len(rows[0]))
        con.executemany(f"INSERT INTO raw.{name} VALUES ({placeholders});", rows)
    parquet = RAW_DIR / f"{name}.parquet"
    con.execute(f"COPY raw.{name} TO '{parquet.as_posix()}' (FORMAT PARQUET);")
    print(f"  ok {name:<12} {len(rows):>8,} rows  ->  raw.{name}  +  {parquet.name}")


def main() -> int:
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    db = PLATFORM_ROOT / os.environ.get("DUCKDB_PATH", "warehouse.duckdb")
    print(f"-> Generating synthetic OLTP data -> {db}\n"
          f"   ({N_CUSTOMERS} customers, {N_PRODUCTS} products, {N_ORDERS} orders)\n")

    customers = gen_customers()
    products = gen_products()
    orders, items = gen_orders_and_items(products)

    con = duckdb.connect(str(db))
    con.execute("CREATE SCHEMA IF NOT EXISTS raw;")
    load(con, "customers", customers)
    load(con, "products", products)
    load(con, "orders", orders)
    load(con, "order_items", items)
    con.close()

    total = len(customers) + len(products) + len(orders) + len(items)
    print(f"\nOK Generated {total:,} rows across 4 raw tables.")
    print("   Next: build the dbt models on top of schema `raw`.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
