
-- ===== Drop 2 DB=====
USE master;
GO
ALTER DATABASE ECommerPipeline_Oltp SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE ECommerPipeline_Oltp;
ALTER DATABASE ECommerPipeline_Olap SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE ECommerPipeline_Olap;
GO

-- ===== Block 1: Chạy 3 lần (warm cache) =====

USE ECommerPipeline_Olap;
SET STATISTICS TIME ON;

SELECT  p.Category, COUNT(DISTINCT f.OrderId) AS OrderCount, SUM(f.LineTotal) AS Revenue
FROM    fact.SalesOrderItem f
JOIN    dim.Product p ON p.ProductKey = f.ProductKey
JOIN    dim.Date    d ON d.DateKey    = f.DateKey
WHERE   d.[Date] >= DATEADD(DAY, -90, GETUTCDATE())
GROUP BY p.Category;

-- ===== Block 2: Chạy 3 lần (warm cache) =====
USE ECommerPipeline_Oltp;
SET STATISTICS TIME ON;

SELECT  p.Category, COUNT(DISTINCT o.Id) AS OrderCount, SUM(oi.LineTotal) AS Revenue
FROM    OrderItems oi
JOIN    Orders   o ON o.Id = oi.OrderId
JOIN    Products p ON p.Id = oi.ProductId
WHERE   o.OrderDate >= DATEADD(DAY, -90, GETUTCDATE())
GROUP BY p.Category;
-----------------==================
USE ECommerPipeline_Olap;

ALTER INDEX CCI_SalesOrderItem ON fact.SalesOrderItem
    REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);

	---======= Verify rowgroup đã COMPRESSED
	
USE ECommerPipeline_Olap;

SELECT
    state_description,
    total_rows,
    deleted_rows,
    size_in_bytes / 1024.0 AS SizeKB
FROM sys.column_store_row_groups
WHERE object_id = OBJECT_ID('fact.SalesOrderItem');
--==Block 1 — Query OLAP
USE ECommerPipeline_Olap;
SET STATISTICS TIME ON;

SELECT  p.Category,
        COUNT(DISTINCT f.OrderId) AS OrderCount,
        SUM(f.LineTotal)          AS Revenue
FROM    fact.SalesOrderItem f
JOIN    dim.Product p ON p.ProductKey = f.ProductKey
JOIN    dim.Date    d ON d.DateKey    = f.DateKey
WHERE   d.[Date] >= DATEADD(DAY, -90, GETUTCDATE())
GROUP BY p.Category
ORDER BY Revenue DESC;
--==Block 2 — Query OLTP (cùng logic)
USE ECommerPipeline_Oltp;
SET STATISTICS TIME ON;

SELECT  p.Category,
        COUNT(DISTINCT o.Id) AS OrderCount,
        SUM(oi.LineTotal)    AS Revenue
FROM    OrderItems oi
JOIN    Orders   o ON o.Id = oi.OrderId
JOIN    Products p ON p.Id = oi.ProductId
WHERE   o.OrderDate >= DATEADD(DAY, -90, GETUTCDATE())
GROUP BY p.Category
ORDER BY Revenue DESC;
--============
USE ECommerPipeline_Oltp;
SELECT COUNT(*) AS OrdersInOltp FROM Orders;
SELECT COUNT(*) AS ItemsInOltp  FROM OrderItems;
SELECT MIN(OrderDate) AS Min, MAX(OrderDate) AS Max FROM Orders;