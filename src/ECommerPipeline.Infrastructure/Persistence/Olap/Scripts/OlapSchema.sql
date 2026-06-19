-- ============================================================
-- OLAP Schema — Medallion Architecture (Bronze / Silver / Gold)
--
-- Bronze: raw copy from OLTP, no transformation
-- Silver: cleaned + star schema (fact + dimensions)
-- Gold:   pre-aggregated for fast dashboard queries
--
-- Dimensions use SCD Type 2 — keep historical versions so reports
-- for past periods show the customer/product state at THAT time.
-- ============================================================

IF SCHEMA_ID(N'bronze') IS NULL EXEC(N'CREATE SCHEMA bronze');
IF SCHEMA_ID(N'dim')    IS NULL EXEC(N'CREATE SCHEMA dim');     -- silver dimensions
IF SCHEMA_ID(N'fact')   IS NULL EXEC(N'CREATE SCHEMA fact');    -- silver facts
IF SCHEMA_ID(N'gold')   IS NULL EXEC(N'CREATE SCHEMA gold');
IF SCHEMA_ID(N'etl')    IS NULL EXEC(N'CREATE SCHEMA etl');
IF SCHEMA_ID(N'dq')     IS NULL EXEC(N'CREATE SCHEMA dq');      -- data quality

-- ============================================================
-- BRONZE LAYER — raw landing zone
-- One row per source row, no transformation.
-- ============================================================
IF OBJECT_ID(N'bronze.OrderItem_Raw', N'U') IS NULL
BEGIN
    CREATE TABLE bronze.OrderItem_Raw (
        BronzeKey         BIGINT IDENTITY PRIMARY KEY,
        OrderItemId       BIGINT NOT NULL,
        OrderId           BIGINT NOT NULL,
        CustomerId        BIGINT NOT NULL,
        ProductId         BIGINT NOT NULL,
        OrderDate         DATETIME2 NOT NULL,
        Quantity          INT NOT NULL,
        UnitPrice         DECIMAL(18,2) NOT NULL,
        LineTotal         DECIMAL(18,2) NOT NULL,
        IngestedAt        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        SourceSystem      VARCHAR(50) NOT NULL DEFAULT 'OLTP_EFCore'
    );
    CREATE UNIQUE INDEX UX_Bronze_OrderItemId ON bronze.OrderItem_Raw(OrderItemId);
    CREATE INDEX IX_Bronze_IngestedAt ON bronze.OrderItem_Raw(IngestedAt);
END;

-- ============================================================
-- SILVER LAYER — Star schema (cleaned + conformed)
-- ============================================================

-- dim.Date — type 0 (immutable calendar)
IF OBJECT_ID(N'dim.Date', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Date (
        DateKey      INT          NOT NULL PRIMARY KEY,
        [Date]       DATE         NOT NULL,
        [Year]       SMALLINT     NOT NULL,
        [Quarter]    TINYINT      NOT NULL,
        [Month]      TINYINT      NOT NULL,
        MonthName    VARCHAR(20)  NOT NULL,
        [Day]        TINYINT      NOT NULL,
        DayOfWeek    TINYINT      NOT NULL,
        IsWeekend    BIT          NOT NULL
    );
    CREATE INDEX IX_Date_YearMonth ON dim.Date([Year], [Month]);
END;

-- dim.Customer — SCD Type 2 (keep historical versions)
IF OBJECT_ID(N'dim.Customer', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Customer (
        CustomerKey   INT IDENTITY PRIMARY KEY,           -- surrogate (1 customer = N rows)
        CustomerId    BIGINT       NOT NULL,              -- natural key from OLTP
        FullName      NVARCHAR(200) NOT NULL,
        Email         NVARCHAR(200) NOT NULL,
        City          NVARCHAR(100) NULL,

        -- ⭐ SCD Type 2 columns
        ValidFrom     DATETIME2     NOT NULL,
        ValidTo       DATETIME2     NULL,                  -- NULL = currently valid
        IsCurrent     BIT           NOT NULL,              -- denormalized for fast filter
        Version       INT           NOT NULL DEFAULT 1,
        RowHash       BINARY(32)    NULL,                  -- SHA256 of tracked cols, detect change cheaply

        EtlLoadedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    );
    -- Only ONE current row per CustomerId
    CREATE UNIQUE INDEX UX_Customer_CurrentVersion
        ON dim.Customer(CustomerId)
        WHERE IsCurrent = 1;
    CREATE INDEX IX_Customer_CustomerId ON dim.Customer(CustomerId);
END;

-- dim.Product — SCD Type 2
IF OBJECT_ID(N'dim.Product', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Product (
        ProductKey    INT IDENTITY PRIMARY KEY,
        ProductId     BIGINT       NOT NULL,
        Sku           VARCHAR(50)  NOT NULL,
        Name          NVARCHAR(300) NOT NULL,
        Category      NVARCHAR(100) NOT NULL,
        Brand         NVARCHAR(100) NULL,

        -- SCD Type 2
        ValidFrom     DATETIME2    NOT NULL,
        ValidTo       DATETIME2    NULL,
        IsCurrent     BIT          NOT NULL,
        Version       INT          NOT NULL DEFAULT 1,
        RowHash       BINARY(32)   NULL,

        EtlLoadedAt   DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_Product_CurrentVersion
        ON dim.Product(ProductId)
        WHERE IsCurrent = 1;
    CREATE INDEX IX_Product_ProductId ON dim.Product(ProductId);
    CREATE INDEX IX_Product_Category ON dim.Product(Category) WHERE IsCurrent = 1;
END;

-- fact.SalesOrderItem — fact references SCD Type 2 dimension SURROGATE keys
-- (i.e., the customer/product state AT THE TIME the order was placed)
IF OBJECT_ID(N'fact.SalesOrderItem', N'U') IS NULL
BEGIN
    CREATE TABLE fact.SalesOrderItem (
        SalesOrderItemKey BIGINT IDENTITY NOT NULL,
        DateKey           INT     NOT NULL,
        CustomerKey       INT     NOT NULL,     -- ← surrogate; the customer state at order time
        ProductKey        INT     NOT NULL,     -- ← surrogate; the product state at order time
        OrderId           BIGINT  NOT NULL,
        OrderItemId       BIGINT  NOT NULL,
        Quantity          INT     NOT NULL,
        UnitPrice         DECIMAL(18,2) NOT NULL,
        LineTotal         DECIMAL(18,2) NOT NULL,
        EtlLoadedAt       DATETIME2     NOT NULL
    );

    CREATE CLUSTERED COLUMNSTORE INDEX CCI_SalesOrderItem
        ON fact.SalesOrderItem;

    CREATE UNIQUE INDEX UX_SalesOrderItem_OrderItemId
        ON fact.SalesOrderItem(OrderItemId);
END;

-- ============================================================
-- GOLD LAYER — pre-aggregated business-ready tables
-- Dashboards query these (5-10ms) instead of recomputing from fact (90ms)
-- Refreshed by ETL after silver loads
-- ============================================================

-- Gold 1: daily sales by category
IF OBJECT_ID(N'gold.DailySalesByCategory', N'U') IS NULL
BEGIN
    CREATE TABLE gold.DailySalesByCategory (
        [Date]         DATE NOT NULL,
        Category       NVARCHAR(100) NOT NULL,
        OrderCount     BIGINT NOT NULL,
        ItemCount      BIGINT NOT NULL,
        TotalRevenue   DECIMAL(18,2) NOT NULL,
        AvgOrderValue  DECIMAL(18,2) NOT NULL,
        RefreshedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_DailySalesByCategory PRIMARY KEY ([Date], Category)
    );
END;

-- Gold 2: monthly top products
IF OBJECT_ID(N'gold.MonthlyTopProducts', N'U') IS NULL
BEGIN
    CREATE TABLE gold.MonthlyTopProducts (
        [Year]         SMALLINT NOT NULL,
        [Month]        TINYINT NOT NULL,
        ProductId      BIGINT NOT NULL,
        Sku            VARCHAR(50) NOT NULL,
        ProductName    NVARCHAR(300) NOT NULL,
        Category       NVARCHAR(100) NOT NULL,
        TotalQuantity  BIGINT NOT NULL,
        TotalRevenue   DECIMAL(18,2) NOT NULL,
        RankInMonth    INT NOT NULL,
        RefreshedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MonthlyTopProducts PRIMARY KEY ([Year], [Month], ProductId)
    );
END;

-- Gold 3: customer LTV (lifetime value) snapshot
IF OBJECT_ID(N'gold.CustomerLifetimeValue', N'U') IS NULL
BEGIN
    CREATE TABLE gold.CustomerLifetimeValue (
        CustomerId       BIGINT NOT NULL PRIMARY KEY,
        FirstOrderDate   DATE NOT NULL,
        LastOrderDate    DATE NOT NULL,
        TotalOrders      BIGINT NOT NULL,
        TotalRevenue     DECIMAL(18,2) NOT NULL,
        AvgOrderValue    DECIMAL(18,2) NOT NULL,
        DaysSinceLastOrder INT NOT NULL,
        RefreshedAt      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

-- Gold 4: sales by payment method (current OLTP order state, refreshed each ETL run)
-- NOTE: sourced from CURRENT order state (not the immutable fact) because
-- PaymentMethod/PaymentStatus are mutable after an order is first ingested.
IF OBJECT_ID(N'gold.SalesByPaymentMethod', N'U') IS NULL
BEGIN
    CREATE TABLE gold.SalesByPaymentMethod (
        PaymentMethod   TINYINT      NOT NULL PRIMARY KEY,   -- 1 COD / 2 VNPay / 3 MoMo
        MethodName      VARCHAR(20)  NOT NULL,
        OrderCount      BIGINT       NOT NULL,
        PaidOrderCount  BIGINT       NOT NULL,
        TotalRevenue    DECIMAL(18,2) NOT NULL,              -- gross (all orders)
        PaidRevenue     DECIMAL(18,2) NOT NULL,              -- only PaymentStatus = Paid
        RefreshedAt     DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

-- Gold 5: order fulfilment funnel (current order-state counts)
IF OBJECT_ID(N'gold.OrderFunnel', N'U') IS NULL
BEGIN
    CREATE TABLE gold.OrderFunnel (
        Stage        VARCHAR(20)  NOT NULL PRIMARY KEY,      -- Placed/Paid/Confirmed/Shipped/Delivered/Cancelled
        StageOrder   TINYINT      NOT NULL,
        OrderCount   BIGINT       NOT NULL,
        RefreshedAt  DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

-- Gold 6: product inventory + turnover (current stock from OLTP + units sold from fact)
IF OBJECT_ID(N'gold.ProductInventory', N'U') IS NULL
BEGIN
    CREATE TABLE gold.ProductInventory (
        ProductId     BIGINT       NOT NULL PRIMARY KEY,
        Sku           VARCHAR(50)  NOT NULL,
        ProductName   NVARCHAR(300) NOT NULL,
        Category      NVARCHAR(100) NOT NULL,
        CurrentStock  INT          NOT NULL,
        UnitsSold     BIGINT       NOT NULL,
        LowStock      BIT          NOT NULL,                 -- CurrentStock below threshold
        RefreshedAt   DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ProductInventory_LowStock ON gold.ProductInventory(LowStock) WHERE LowStock = 1;
END;

-- ============================================================
-- ETL — watermark + run log
-- ============================================================
IF OBJECT_ID(N'etl.Watermark', N'U') IS NULL
BEGIN
    CREATE TABLE etl.Watermark (
        PipelineName       VARCHAR(100) NOT NULL PRIMARY KEY,
        LastProcessedAt    DATETIME2    NOT NULL,
        LastProcessedRowId BIGINT       NOT NULL,
        UpdatedAt          DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'etl.RunLog', N'U') IS NULL
BEGIN
    CREATE TABLE etl.RunLog (
        RunId          BIGINT IDENTITY PRIMARY KEY,
        PipelineName   VARCHAR(100) NOT NULL,
        StartedAt      DATETIME2    NOT NULL,
        CompletedAt    DATETIME2    NULL,
        DurationMs     BIGINT       NULL,
        RowsProcessed  BIGINT       NULL,
        Status         VARCHAR(20)  NOT NULL,            -- Running / Succeeded / Failed
        ErrorMessage   NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_RunLog_PipelineDate ON etl.RunLog(PipelineName, StartedAt DESC);
END;

-- ============================================================
-- DATA QUALITY — test results
-- ============================================================
IF OBJECT_ID(N'dq.TestResults', N'U') IS NULL
BEGIN
    CREATE TABLE dq.TestResults (
        ResultId       BIGINT IDENTITY PRIMARY KEY,
        TestName       VARCHAR(100) NOT NULL,
        Category       VARCHAR(50)  NOT NULL,            -- Uniqueness / Integrity / Freshness / Completeness / Business
        Severity       VARCHAR(20)  NOT NULL,            -- Critical / Warning / Info
        Status         VARCHAR(20)  NOT NULL,            -- Pass / Fail
        ActualValue    NVARCHAR(500) NULL,
        ExpectedValue  NVARCHAR(500) NULL,
        Message        NVARCHAR(MAX) NULL,
        RunAt          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_TestResults_RunAt ON dq.TestResults(RunAt DESC);
    CREATE INDEX IX_TestResults_Status ON dq.TestResults(Status, RunAt DESC) WHERE Status = 'Fail';
END;
