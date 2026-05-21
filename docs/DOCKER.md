# Docker Quickstart

## Yêu cầu
- **Docker Desktop** (Windows/Mac) hoặc Docker Engine + Compose plugin (Linux)
- 4 GB RAM free (SQL Server cần 2 GB tối thiểu)
- Port 80, 1433, 5193 không bị app khác chiếm

## Chạy

```bash
docker compose up -d
```

Lần đầu sẽ mất ~5 phút:
1. Pull image SQL Server + .NET SDK + Node + Nginx
2. Build backend image (restore NuGet, publish .NET)
3. Build frontend image (npm install, vite build, copy vào nginx)
4. Start SQL Server (~30 giây)
5. Backend chờ SQL ready (healthcheck), apply migrations, seed data
6. Frontend serve trên port 80

## Verify

| Service | URL | Dùng để |
|---|---|---|
| Storefront | http://localhost | Browse shop, đặt đơn |
| Admin Dashboard | http://localhost/admin | Xem analytics |
| Scalar API Docs | http://localhost/scalar/v1 | Try API |
| Hangfire UI | http://localhost/hangfire | Xem background jobs |
| Health Check | http://localhost/health | Phải trả "Healthy" |
| API trực tiếp | http://localhost:5193 | Debug (nếu cần) |
| SQL Server | `localhost,1433` user `sa` pwd `YourStrong@Passw0rd` | SSMS inspect |

## Theo dõi log

```bash
docker compose logs -f api        # backend log
docker compose logs -f sql        # SQL Server log
docker compose logs -f frontend   # nginx access log
docker compose logs -f            # all
```

Đợi log của **api** hiện `Now listening on: http://[::]:8080` là sẵn sàng.

## Stop / Reset

```bash
docker compose down               # stop containers, giữ data
docker compose down -v            # stop + xoá luôn SQL data volume
docker compose up -d --build      # rebuild image khi thay đổi code
```

## Tinh chỉnh

### Đổi seed size (mặc định nhỏ cho first-run nhanh)
Sửa `docker-compose.yml` → `api.environment`:
```yaml
Seed__CustomerCount: "5000"
Seed__ProductCount: "1000"
Seed__OrderCount: "100000"
```
Rồi `docker compose down -v && docker compose up -d` (cần xoá volume).

### Đổi password SQL Server
Sửa cả `MSSQL_SA_PASSWORD` (sql service) + connection strings (api service).
SQL Server yêu cầu password mạnh: ≥ 8 ký tự + chữ HOA + chữ thường + số + ký tự đặc biệt.

### Production deploy hints
- Đổi `ASPNETCORE_ENVIRONMENT` → `Production` (đã set)
- Set `Cors__AllowedOrigins` env var với domain thật
- Mount `sql_data` volume vào host path để backup
- Đặt sau reverse proxy (Caddy/Traefik) cho HTTPS
- SQL Server password phải lấy từ secret manager, không hardcode

## Troubleshooting

**"sql exited (1)"** → password không đủ mạnh, hoặc EULA chưa accept. Check env vars.

**"api healthcheck failing"** → SQL chưa migrate xong. Đợi thêm 30-60s rồi check lại.

**"frontend 502 Bad Gateway"** → api chưa lên. `docker compose logs api`.

**Port 80 đang bị chiếm** (IIS, Apache, etc.) → đổi mapping trong compose:
```yaml
frontend:
  ports:
    - "8080:80"
```
Rồi mở http://localhost:8080.
