/* ============================================================================
   create_readonly_user.ecommerce.sql
   Read-only DB principal for the AI Data Analyst when pointed at the
   ECommerPipeline OLAP database (ECommerPipeline_Olap).

   This is the security backstop (layer 3 of defense-in-depth): the Analyst app
   connects as `analyst_ro`, which can ONLY SELECT on the `gold` schema and
   CANNOT write or read other schemas — even if the app-level SQL validator
   had a bug. Schema-level GRANT covers current AND future gold tables.

   Idempotent: safe to run repeatedly (e.g. from a docker init container).
   Dev credentials only.
   ============================================================================ */

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'analyst_ro')
    CREATE LOGIN analyst_ro WITH PASSWORD = 'Readonly#Analyst1', CHECK_POLICY = OFF;
GO

USE ECommerPipeline_Olap;
GO

-- Ensure the gold schema exists so the GRANT below succeeds even if the ETL
-- pipeline has not applied OlapSchema.sql yet. Schema-level grants apply to
-- every object created in the schema later, so no re-grant is needed after ETL.
IF SCHEMA_ID(N'gold') IS NULL EXEC(N'CREATE SCHEMA gold');
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'analyst_ro')
    CREATE USER analyst_ro FOR LOGIN analyst_ro WITH DEFAULT_SCHEMA = gold;
GO

/* Read ONLY on gold. Deliberately NOT db_datareader (would expose dbo/fact/dim/bronze). */
GRANT SELECT ON SCHEMA::gold TO analyst_ro;

/* Defense in depth: explicitly deny any write / DDL / exec on gold. */
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::gold TO analyst_ro;
GO

PRINT 'analyst_ro configured on ECommerPipeline_Olap (SELECT on gold only).';
GO
