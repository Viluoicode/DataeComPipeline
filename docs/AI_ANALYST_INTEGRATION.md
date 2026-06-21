# AI Data Analyst — NL→SQL layer on the Gold layer

The `ai-analyst/` folder is a self-contained .NET 10 service ([upstream repo](https://github.com/Viluoicode/ai-data-analyst)) integrated into this pipeline as a **natural-language → SQL query layer** over the Gold layer of `ECommerPipeline_Olap`.

```
Source → [Pipeline: Bronze → Silver → Gold] → AI Data Analyst → user asks freely
            (this repo's ETL)                  (ai-analyst/)     (VN/EN questions)
```

The pipeline **produces** clean Gold tables; the Analyst **unlocks** them for people who don't write SQL. Both read the same `gold` schema — complementary, not replacing.

## Unified UI — the "Ask Data" chat in the Admin dashboard

Rather than sending users to a separate port, the Analyst is surfaced **inside the React Admin** as a chat assistant:

```
React Admin  /admin/ask  ──POST /api/ask──►  ECommerPipeline.Api (.NET 9)
 (chat UI, JWT)                                   │  proxy (HttpClient "analyst", Admin/Staff only)
                                                  ▼
                                          analyst-api (internal :8080)  ──analyst_ro──►  gold.*
```

- **Frontend:** `frontend/src/pages/AskData.tsx` — chat bubbles, suggested questions, NL summary, result table, collapsible generated SQL. Linked in the sidebar as **"Ask Data (AI)"**.
- **Backend proxy:** `POST /api/ask` in `Program.cs` (`RequireRole("Admin","Staff")`) forwards to the analyst via the `"analyst"` `HttpClient` (`Analyst:BaseUrl`, default `http://localhost:8090` local / `http://analyst-api:8080` in Docker).
- **Why proxy, not call analyst directly from the browser:** the analyst stays internal (never CORS-exposed), and every question inherits this API's JWT auth + correlation-id logging. The strong NL→SQL safety (AST validation, `analyst_ro`) is reused as-is — not re-implemented.

## What was integrated (handoff §8)

1. **Copied** the Analyst project into `ai-analyst/` (own .NET 10 solution, src/tests/db/Dockerfile/eval). It is NOT part of `ECommerPipeline.sln` — it builds independently via its own Dockerfile.
2. **`ai-analyst/config/schema.ecommerce.json`** — whitelist + prompt source of truth pointing at the pipeline's 3 Gold tables: `gold.DailySalesByCategory`, `gold.MonthlyTopProducts`, `gold.CustomerLifetimeValue`. Few-shot examples are e-commerce/VND.
3. **`ai-analyst/db/create_readonly_user.ecommerce.sql`** — idempotent script creating `analyst_ro` on `ECommerPipeline_Olap` (SELECT on `gold` only; writes/DDL denied). Schema-level GRANT covers future gold tables.
4. **`docker-compose.yml`** (root) — two new services:
   - `analyst-db-init` — one-shot, runs the read-only-user script after `api` is healthy (so the OLAP DB + gold schema exist).
   - `analyst-api` — the NL→SQL service, connects as `analyst_ro`, `Analyst__SchemaConfigPath=config/schema.ecommerce.json`, exposed on **http://localhost:8090**.
5. **`ai-analyst/eval/questions.ecommerce.jsonl`** — golden eval questions for the e-commerce Gold data.

## Run

```bash
docker compose up -d --build          # brings up sql, api, frontend, jaeger, analyst-db-init, analyst-api
docker compose logs -f analyst-api    # wait for "Now listening on :8080"
```

Then:
- **http://localhost:8090** — Ask-data demo UI (type a question, see generated SQL + rows + summary)
- `POST http://localhost:8090/ask` — `{ "question": "Doanh thu theo category?", "includeSummary": true }`
- `GET http://localhost:8090/health`

> The Analyst only returns data after the pipeline's ETL has populated the Gold tables (trigger via `/admin/stress` → Trigger ETL, or wait for the recurring job). Before that, queries succeed but return empty.

## Provider

Default `Analyst__Provider=Offline` (deterministic canned SQL, zero API keys — safety still fully enforced). For real NL→SQL set `Analyst__Provider=OpenAI` + `Analyst__OpenAI__ApiKey` (or point `Analyst__OpenAI__BaseUrl` at a local Ollama). See `ai-analyst/README.md`.

## Safety model (do NOT weaken)

Four layers of defense-in-depth:
1. Prompt only tells the model the whitelisted gold tables/columns.
2. **AST validator** (ScriptDom): single SELECT, whitelist, no INTO/OPENROWSET/cross-DB, injected TOP cap.
3. **`analyst_ro` DB principal**: SELECT on gold only; writes/DDL denied — the real backstop.
4. Resource guard: command timeout + reader row cap.

Fail-closed: nothing executes unless the validator passes. When changing the validator, add a test in `ai-analyst/tests/Analyst.Tests/SqlValidatorTests.cs`.

### Endpoint hardening (the `/api/ask` proxy)

Beyond the NL→SQL safety above, the proxy in `ECommerPipeline.Api` adds production guards (P0):
- **Rate limiting** — per-user (15 req/min), HTTP 429 on excess. LLM calls cost money and are abusable. (`AddRateLimiter` + `.RequireRateLimiting("ai-ask")`)
- **Caching** — identical questions are served from `IMemoryCache` (10-min TTL) without a second LLM call.
- **Audit logging** — every question logs `user → status → latency` with the request correlation id.
- **Secrets fail-fast** — the app refuses to boot in Production with the bundled dev `Jwt:Secret`.

See `docs/DECISIONS.md` → Production Roadmap for the full P0/P1/P2 list.

## Why Gold (not Silver/raw)

The Gold tables are denormalized, business-ready, and single-schema — ideal NL→SQL targets (clean column names, no SCD2 surrogate-key joins). Questions about data not in Gold (e.g. per-store breakdown) are correctly refused — that's the safety story working, not a bug. To widen coverage later, add the Silver star schema (`fact.SalesOrderItem` + `dim.*`) to `schema.ecommerce.json` and grant `analyst_ro` SELECT on `fact`/`dim`.

## Standalone mode

`ai-analyst/docker-compose.yml` still runs the Analyst against its own F&B demo DB (`schema.fnb.json`) independently — useful for testing the Analyst in isolation. The integrated mode above uses the **root** compose + the e-commerce schema.
