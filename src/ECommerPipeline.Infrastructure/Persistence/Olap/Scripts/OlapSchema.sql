-- OLAP Star Schema for Sales Analytics
-- Run once on the OLAP database (separate DB or same instance, different schema)

IF SCHEMA_ID(N'dim') IS NULL EXEC(N'CREATE SCHEMA dim');
IF SCHEMA_ID(N'fact') IS NULL EXEC(N'CREATE SCHEMA fact');
IF SCHEMA_ID(N'etl')  IS NULL EXEC(N'CREATE SCHEMA etl');

-- ============================================================
-- DIMENSION TABLES (row-store, small, frequently joined)
-- ============================================================
IF OBJECT_ID(N'dim.Date', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Date (
        DateKey      INT          NOT NULL PRIMARY KEY,  -- yyyymmdd
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

IF OBJECT_ID(N'dim.Customer', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Customer (
        CustomerKey   INT IDENTITY PRIMARY KEY,
        CustomerId    BIGINT       NOT NULL UNIQUE,  -- source OLTP id
        FullName      NVARCHAR(200) NOT NULL,
        Email         NVARCHAR(200) NOT NULL,
        City          NVARCHAR(100) NULL,
        EtlLoadedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dim.Product', N'U') IS NULL
BEGIN
    CREATE TABLE dim.Product (
        ProductKey    INT IDENTITY PRIMARY KEY,
        ProductId     BIGINT       NOT NULL UNIQUE,
        Sku           VARCHAR(50)  NOT NULL,
        Name          NVARCHAR(300) NOT NULL,
        Category      NVARCHAR(100) NOT NULL,
        Brand         NVARCHAR(100) NULL,
        EtlLoadedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Product_Category ON dim.Product(Category);
END;

-- ============================================================
-- FACT TABLE (columnstore — analytical queries scan millions of rows)
-- ============================================================
IF OBJECT_ID(N'fact.SalesOrderItem', N'U') IS NULL
BEGIN
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

    -- Clustered columnstore: massive compression + fast aggregate scans
    CREATE CLUSTERED COLUMNSTORE INDEX CCI_SalesOrderItem
        ON fact.SalesOrderItem;

    -- Non-clustered B-tree for point lookup (idempotent ETL)
    CREATE UNIQUE INDEX UX_SalesOrderItem_OrderItemId
        ON fact.SalesOrderItem(OrderItemId);
END;

-- ============================================================
-- ETL WATERMARK (tracks last extracted OLTP row)
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
