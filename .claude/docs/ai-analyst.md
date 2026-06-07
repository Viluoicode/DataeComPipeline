# AI Data Analyst — pipeline-side integration

Agent-facing reference for the NL→SQL layer **as wired into this pipeline**. The
analyst service itself is a self-contained .NET 10 project under `ai-analyst/` —
for its internals (validator, providers, prompt) read `ai-analyst/CLAUDE.md` and
`ai-analyst/.claude/docs/*`. This file covers only the glue that lives in the
pipeline.

## Request flow

```
React Admin /admin/ask ──POST /api/ask──► ECommerPipeline.Api (.NET 9)
 (chat UI, JWT)            proxy (Admin/Staff, rate-limited, cached, audited)
                                  │  HttpClient "analyst" (Analyst:BaseUrl)
                                  ▼
                          analyst-api (.NET 10, internal) ──analyst_ro──► gold.*
```

The browser never talks to the analyst directly — it goes through `/api/ask` so
the analyst stays internal and inherits the API's JWT auth + correlation id.

## Where the code lives (pipeline side)

- **Proxy endpoint** — `src/ECommerPipeline.Api/Program.cs`, `POST /api/ask`
  (`RequireRole("Admin","Staff")`, `RequireRateLimiting("ai-ask")`) and
  `GET /api/admin/ai-metrics` (Admin only).
- **HttpClient "analyst"** — registered in `Program.cs`; base URL from
  `Analyst:BaseUrl` (default `http://localhost:8090`; Docker: `http://analyst-api:8080`).
- **Metrics** — `src/ECommerPipeline.Api/Observability/AiMetrics.cs` (singleton).
- **Frontend** — `frontend/src/pages/AskData.tsx`, `frontend/src/api/analyst.ts`,
  route `/admin/ask`, sidebar entry in `AppLayout.tsx`.
- **Schema whitelist** — `ai-analyst/config/schema.ecommerce.json` (the 3 Gold
  tables). This is the single source of truth for prompt + validator.
- **Read-only DB principal** — `ai-analyst/db/create_readonly_user.ecommerce.sql`
  (creates `analyst_ro`: SELECT on `gold` only; writes/DDL denied).
- **Compose services** — `analyst-db-init` (one-shot, after `api` healthy) +
  `analyst-api` (port 8090) in the root `docker-compose.yml`.

## Config keys

| Key | Purpose |
|---|---|
| `Analyst:BaseUrl` (pipeline API) | where `/api/ask` proxies to |
| `ConnectionStrings:Analyst` (analyst) | OLAP DB as `analyst_ro` |
| `Analyst:SchemaConfigPath` (analyst) | `config/schema.ecommerce.json` |
| `Analyst:Provider` (analyst) | `Offline` (default, zero-key) / `OpenAI` / `AzureOpenAI` |

## Safety model (4 layers, fail-closed) — NEVER weaken

1. Prompt only describes whitelisted gold tables/columns.
2. **AST validator** (ScriptDom): single SELECT, table/column whitelist, no
   INTO/OPENROWSET/cross-DB, injected TOP cap.
3. **`analyst_ro`**: SELECT on `gold` only — the real backstop even if (2) has a bug.
4. Resource guard: command timeout + row cap.

Endpoint hardening (P0/P1) on the proxy: per-user rate limit (15/min → 429),
`IMemoryCache` (10-min TTL, skips repeat LLM calls), audit log
(`user → status → latency`), and the `ai-metrics` endpoint (refusal/cache-hit
rate, avg latency).

## Constraints

- **Never weaken the validator** to make a query pass. Add a test in
  `ai-analyst/tests/Analyst.Tests/SqlValidatorTests.cs` when editing it.
- **Never give the app a write-capable DB account.** Queries run as `analyst_ro`.
- **`schema.ecommerce.json` is the source of truth** — change the schema there,
  not in code. The Offline provider also reads its `fewShot`.
- **CI gate:** `.github/workflows/ai-analyst.yml` runs the eval harness; a safety
  regression (a malicious query the validator lets through) fails the build.

## Run locally (without Docker — this machine is RAM-constrained)

```bash
# 1. pipeline API (creates DBs + ETL) → :5193
dotnet run --project src/ECommerPipeline.Api
# 2. create analyst_ro on the pipeline OLAP DB
sqlcmd -S localhost -E -i ai-analyst/db/create_readonly_user.ecommerce.sql
# 3. analyst service → :8090 (analyst_ro, e-commerce schema)
cd ai-analyst && ConnectionStrings__Analyst="Server=localhost;Database=ECommerPipeline_Olap;User Id=analyst_ro;Password=Readonly#Analyst1;TrustServerCertificate=True;Encrypt=False" \
  Analyst__SchemaConfigPath="config/schema.ecommerce.json" \
  dotnet run --project src/Analyst.Api --urls http://localhost:8090
```

Human-facing version (run steps, rationale): `docs/AI_ANALYST_INTEGRATION.md`.
Production roadmap (P0/P1/P2): `docs/DECISIONS.md`.
