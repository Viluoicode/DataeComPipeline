# 🚀 Deploy lên Azure (free tier)

> Hướng dẫn deploy ECommerPipeline lên Azure **miễn phí**: 1 App Service (F1) serve
> cả API + React SPA, 1 Azure SQL Database (free serverless) chứa toàn bộ data.
>
> Kiến trúc khi deploy:
> ```
> Browser → Azure App Service (F1 free)
>             ├── serve React SPA (wwwroot)
>             └── API /api/* /hub/* /hangfire /scalar
>                   └── Azure SQL Database (free serverless, 1 DB, schema-separated)
> ```

---

## 0. Vì sao config khác local?

| | Local / Docker | Azure |
|---|---|---|
| Số database | 3 (Oltp/Olap/Hangfire) | **1** (gộp, tách bằng schema) |
| Tạo DB | App tự `CREATE DATABASE` | Portal tạo sẵn, app skip (`Database:AutoCreate=false`) |
| Frontend | nginx riêng | API serve từ `wwwroot` |
| Seed | 5000-100k orders | 1000 (nhỏ, hợp F1 quota) |
| Hangfire cron | ETL mỗi 5 phút | ETL mỗi giờ (tiết kiệm CPU) |
| Connection encrypt | `Encrypt=False` | `Encrypt=True` (Azure bắt buộc) |

Tất cả khác biệt này đã được code hoá qua **env vars** — không phải sửa code, chỉ set config trên Azure. `appsettings.Production.json` đã có sẵn seed nhỏ + cron thưa + `Database:AutoCreate=false`.

---

## 1. Chuẩn bị

- **Azure account** free: https://azure.microsoft.com/free (cần thẻ verify, KHÔNG charge ở free tier)
- **Azure CLI**: https://learn.microsoft.com/cli/azure/install-azure-cli
- Đăng nhập:
  ```bash
  az login
  ```

Đặt biến (đổi `<...>` theo ý bạn, tên phải globally unique):
```bash
RG=ecompipeline-rg
LOCATION=southeastasia
SQL_SERVER=ecompipeline-sql-<your-suffix>
SQL_DB=ecompipeline
SQL_ADMIN=sqladmin
SQL_PASSWORD='Str0ng@Passw0rd!<change-me>'
APP=ecompipeline-api-<your-suffix>
```

---

## 2. Resource group

```bash
az group create --name $RG --location $LOCATION
```

---

## 3. Azure SQL Database (free serverless)

```bash
# SQL logical server
az sql server create \
  --name $SQL_SERVER \
  --resource-group $RG \
  --location $LOCATION \
  --admin-user $SQL_ADMIN \
  --admin-password "$SQL_PASSWORD"

# Database — FREE serverless General Purpose tier
az sql db create \
  --resource-group $RG \
  --server $SQL_SERVER \
  --name $SQL_DB \
  --edition GeneralPurpose \
  --compute-model Serverless \
  --family Gen5 \
  --capacity 1 \
  --use-free-limit true \
  --free-limit-exhaustion-behavior AutoPause \
  --backup-storage-redundancy Local

# Firewall: cho phép Azure services (App Service) connect
az sql server firewall-rule create \
  --resource-group $RG \
  --server $SQL_SERVER \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

> `--use-free-limit true` là chìa khoá: 100,000 vCore-giây + 32GB miễn phí/tháng.
> Khi hết quota, DB **auto-pause** (không charge). Lần request kế tiếp sẽ resume (~30s cold start).

**Connection string** (dùng cho cả 3 key Oltp/Olap/Hangfire):
```
Server=tcp:<SQL_SERVER>.database.windows.net,1433;Database=ecompipeline;User Id=sqladmin;Password=<SQL_PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
```

---

## 4. App Service (F1 free, .NET 9)

```bash
# App Service plan — F1 FREE
az appservice plan create \
  --name ecompipeline-plan \
  --resource-group $RG \
  --sku F1 \
  --is-linux

# Web app với .NET 9 runtime
az webapp create \
  --resource-group $RG \
  --plan ecompipeline-plan \
  --name $APP \
  --runtime "DOTNETCORE:9.0"
```

---

## 5. Cấu hình App Service (env vars)

```bash
CONN="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=$SQL_DB;User Id=$SQL_ADMIN;Password=$SQL_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

az webapp config appsettings set \
  --resource-group $RG \
  --name $APP \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__AutoCreate=false \
    ConnectionStrings__OltpConnection="$CONN" \
    ConnectionStrings__OlapConnection="$CONN" \
    ConnectionStrings__HangfireConnection="$CONN" \
    Jwt__Secret="$(openssl rand -base64 48)" \
    Jwt__Issuer="ECommerPipeline" \
    Jwt__Audience="ECommerPipeline.Client" \
    Seed__CustomerCount=200 \
    Seed__ProductCount=50 \
    Seed__OrderCount=1000 \
    Jobs__EtlCron="0 * * * *" \
    Jobs__CompressCron="0 3 * * *" \
    Jobs__DataQualityCron="30 * * * *"
```

> **Cùng 1 connection string cho cả 3 key** — đây là cách gộp 3 DB thành 1 trên Azure.
> OLTP tables ở schema `dbo`, OLAP ở `bronze/dim/fact/gold/etl/dq`, Hangfire ở `HangFire` — không đụng nhau.
> `Jwt__Secret` random 48 bytes. Frontend gọi `/api` relative (cùng origin) nên KHÔNG cần set CORS hay VITE_API_URL.

---

## 6. Deploy bằng GitHub Actions

### 6.1 Lấy publish profile
```bash
az webapp deployment list-publishing-profiles \
  --resource-group $RG --name $APP --xml
```
Copy toàn bộ XML output.

### 6.2 Add vào GitHub Secrets
Repo → **Settings → Secrets and variables → Actions → New repository secret**:
- Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
- Value: dán XML vừa copy

### 6.3 Sửa tên app trong workflow
Mở [`.github/workflows/deploy-azure.yml`](../.github/workflows/deploy-azure.yml), đổi:
```yaml
AZURE_WEBAPP_NAME: ecompipeline-api-<your-suffix>   # ← tên $APP của bạn
```
Commit + push.

### 6.4 Chạy deploy
GitHub repo → tab **Actions** → **Deploy to Azure** → **Run workflow**.

Workflow sẽ:
1. Build frontend (`npm run build`)
2. Copy `frontend/dist/*` → `src/ECommerPipeline.Api/wwwroot/`
3. `dotnet publish` API (kèm wwwroot)
4. Deploy lên App Service

---

## 7. First startup

Lần đầu app chạy, `DatabaseInitializer` tự động (trên DB Azure đã tạo sẵn):
1. Skip CREATE DATABASE (vì `Database:AutoCreate=false`)
2. Apply EF migrations → tạo OLTP tables (`dbo.*`) + RefreshTokens
3. Apply OLAP schema → tạo `bronze/dim/fact/gold/etl/dq`
4. Seed 200 customers + 50 products + 1000 orders + 2 demo account

⏱ Mất ~1-2 phút. Theo dõi log:
```bash
az webapp log tail --resource-group $RG --name $APP
```

---

## 8. Verify

Mở `https://<APP>.azurewebsites.net`:
| URL | Gì |
|---|---|
| `/` | Storefront landing |
| `/admin` | Dashboard (login `admin@ecom.com` / `admin123`) |
| `/scalar/v1` | API docs |
| `/health` | Phải trả "Healthy" |

> ⚠️ Lần đầu truy cập sau khi idle: App Service F1 + SQL serverless đều "ngủ" → cold start ~30-60s. Bình thường cho free tier. F5 lại sau đó nhanh.

---

## 9. Giới hạn F1 free (biết để không hoảng)

| Giới hạn | Giá trị | Ảnh hưởng |
|---|---|---|
| CPU | 60 phút/ngày | ETL hourly + seed nhỏ → đủ. Đừng stress test nặng. |
| RAM | 1 GB | OK với app này |
| Always On | ❌ không có | App ngủ sau ~20 phút không traffic → cold start lần sau |
| Custom domain SSL | ✅ | `*.azurewebsites.net` có HTTPS sẵn |

→ Demo cho recruiter hoàn toàn ổn. Nếu cần always-on mượt → nâng **B1 (~$13/tháng)**:
```bash
az appservice plan update --name ecompipeline-plan --resource-group $RG --sku B1
az webapp config set --resource-group $RG --name $APP --always-on true
```

---

## 10. Cleanup (xoá hết để khỏi tốn tiền)

```bash
az group delete --name $RG --yes --no-wait
```
Xoá nguyên resource group = xoá sạch mọi thứ.

---

## 11. Troubleshooting

**App lỗi 500 lúc startup** → `az webapp log tail`. Thường do connection string sai (thiếu `Encrypt=True`) hoặc firewall chưa allow Azure services.

**"Cannot open server ... requested by the login"** → firewall rule chưa tạo, hoặc IP App Service bị chặn. Chạy lại lệnh firewall ở §3.

**SQL "free limit exhausted"** → hết 100k vCore-sec tháng này. DB auto-pause. Đợi sang tháng hoặc nâng tier.

**Frontend trắng / 404 route** → wwwroot chưa có `index.html`. Check workflow bước "Copy frontend build" có chạy không. SPA fallback chỉ active khi `wwwroot/index.html` tồn tại.

**Cold start chậm 30-60s** → bình thường với F1 + serverless SQL. Không phải bug.
