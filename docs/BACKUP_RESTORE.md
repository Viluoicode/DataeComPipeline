# Backup & restore runbook

The OLTP database (`ECommerPipeline_Oltp`) is the system of record — orders,
customers, products, payments. The OLAP database is **derived** (rebuilt from OLTP
by the ETL), so backing up OLTP is sufficient for disaster recovery.

## Automated backups

`BackupDatabaseJob` (Hangfire recurring, `Jobs:BackupCron`, default **04:00 UTC**)
runs `BACKUP DATABASE ... TO DISK` to **`Backup:Directory`**.

- It **no-ops** when `Backup:Directory` is unset → local/dev needs nothing.
- In Docker, `Backup__Directory=/var/opt/mssql/backups` and that path is a named
  volume (`backups`) on the SQL container, so `.bak` files persist across restarts.
- Trigger manually from the Hangfire dashboard (`/hangfire` → Recurring jobs →
  `db-backup` → Trigger now).

File name: `ECommerPipeline_Oltp_<yyyyMMdd_HHmmss>.bak`.

### Off-site copies (recommended for real deployments)
The volume lives on one host — copy backups off-box. Examples:

```bash
# Pull a backup out of the container/volume to the host
docker cp ecom-sql:/var/opt/mssql/backups ./backups-local

# Sync off-site (S3-compatible) with rclone (configure a remote first)
rclone copy ./backups-local myremote:ecompipeline-backups
```

Schedule the off-site sync via host cron. Define your **RPO** (max data loss — with
nightly backups, up to 24h) and **RTO** (restore time — minutes for this DB size).

## Restore

```bash
# 1. Copy the .bak into the SQL container if it isn't already on the volume
docker cp ./ECommerPipeline_Oltp_20260618_040000.bak ecom-sql:/var/opt/mssql/backups/

# 2. Restore (WITH REPLACE overwrites the live DB — take it out of use first)
docker exec -it ecom-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourStrong@Passw0rd" -No -Q \
  "RESTORE DATABASE [ECommerPipeline_Oltp] FROM DISK = N'/var/opt/mssql/backups/ECommerPipeline_Oltp_20260618_040000.bak' WITH REPLACE, RECOVERY;"
```

3. Restart the API. It will apply any pending EF migrations, then the ETL rebuilds
   the OLAP/Gold layer from the restored OLTP data (no separate OLAP restore needed).

## Verify a backup

```bash
docker exec -it ecom-sql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourStrong@Passw0rd" -No -Q \
  "RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backups/<file>.bak';"
```

## Notes
- Backups are uncompressed (`WITH INIT`, no `COMPRESSION`) for edition portability;
  add `COMPRESSION` on Developer/Enterprise editions to shrink files.
- The Hangfire + Olap databases are **not** backed up — Hangfire is transient job
  state and OLAP is rebuildable from OLTP.
