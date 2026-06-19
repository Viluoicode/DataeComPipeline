# Observability & reliability (Phase 6)

Builds on the existing Serilog + OpenTelemetry tracing with **metrics**, **richer
health checks**, **Redis** (distributed cache + SignalR backplane for horizontal
scale), and **automated backups**. Everything degrades gracefully — local/dev runs
with zero extra setup; the full stack lights up under `docker compose up`.

## Metrics (Prometheus + Grafana)

The API exposes OpenTelemetry metrics at **`GET /metrics`** (Prometheus format):

- **ASP.NET Core** — request rate, duration, active requests, response status.
- **Runtime** — GC, heap, thread pool, exceptions.
- **HttpClient** — outbound calls (e.g. to the AI Analyst).
- **Custom business meter** (`ECommerPipeline.Business`):
  - `ecom_orders_created_total` — orders created.
  - `ecom_payments_total{outcome="success|failed"}` — online payment outcomes.

`docker compose up -d` starts:
- **Prometheus** → http://localhost:9090 (scrapes `api:8080/metrics` every 15s).
- **Grafana** → http://localhost:3000 (login `admin`/`admin`; Prometheus datasource
  auto-provisioned). Build panels, e.g. payment success rate:
  `sum(rate(ecom_payments_total{outcome="success"}[5m])) / sum(rate(ecom_payments_total[5m]))`.

> SQL statement text is included in **traces** only in Development (PII safety, Phase 5).

## Health checks

`GET /health` aggregates:

| Check | Status on problem | Notes |
|---|---|---|
| `oltp` / `olap` (SQL Server) | **Unhealthy** (503) | DB down is a real outage |
| `hangfire` | **Degraded** (200) | no background-job server alive |
| `etl-freshness` | **Degraded** (200) | watermark older than `Health:EtlMaxAgeMinutes` (default 90); "no data yet" = Healthy |

The job/ETL checks are Degraded-only on purpose so the container probe (and
`analyst-db-init` which waits on api health) is never blocked by a stale ETL.

## Redis (scale-out)

Set `ConnectionStrings:Redis` (docker-compose sets `redis:6379`) to enable:
- **Distributed cache** for AI answers (`/api/ask`) shared across API instances.
- **SignalR backplane** so dashboard/notification events fan out across instances.

Unset → in-memory `IDistributedCache` + single-instance SignalR (dev default). This
is what makes running **multiple API replicas** behind a load balancer correct.

## Tracing in production

The dev stack uses Jaeger all-in-one (in-memory, no retention). For production, point
`Otel:Endpoint` at a persistent backend (Jaeger + Badger/Elasticsearch, or Grafana
Tempo). The app only emits OTLP — the backend is swappable via config.

## What's measured where

| Concern | Tool | URL |
|---|---|---|
| Metrics / alerting | Prometheus + Grafana | :9090 / :3000 |
| Traces | Jaeger (dev) / Tempo (prod) | :16686 |
| Logs | Serilog → console/Seq | (Seq :5341 if configured) |
| Job runs | Hangfire dashboard | /hangfire |
| Data quality | `dq.TestResults` + SignalR alert | /admin |
