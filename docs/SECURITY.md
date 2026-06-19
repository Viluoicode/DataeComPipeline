# Security hardening (Phase 5)

Production-readiness hardening layered on top of the existing JWT auth + refresh
tokens + BCrypt. Defaults keep local/dev frictionless; protections engage in
Production.

## What's enforced

| Area | Control | Where |
|---|---|---|
| **Brute force** | `/api/auth/login` + `/api/auth/register` rate-limited to **10 req/min per IP** | `RequireRateLimiting("auth")` |
| **Abuse / scraping** | Global fallback limiter **300 req/min per IP** (exempts `/health`, `/hangfire`, `/hub`) | `GlobalLimiter` |
| **LLM cost** | `/api/ask` **15 req/min per user** (existing) | `RequireRateLimiting("ai-ask")` |
| **Security headers** | `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`; CSP + HSTS in prod | `SecurityHeadersMiddleware` |
| **Hangfire dashboard** | Open in dev; **HTTP Basic auth** in prod (or denied if unconfigured) | `HangfireDashboardAuthFilter` |
| **Trace PII** | SQL text in OpenTelemetry spans **only in Development** | `SetDbStatementForText` |
| **Order IDOR** | Non-staff can only create / list / view **their own** orders | order endpoints in `Program.cs` |
| **Weak secrets** | Boot **fails** in Production if `Jwt:Secret` is the dev default/<32 chars, or a connection string carries the bundled dev SA password | startup guards |

## Authorization model (orders)

- `POST /api/orders` — requires auth. Non-staff callers have `CustomerId` forced to
  their own id (a customer can't bill an order to someone else). Staff/Admin keep
  the requested id (manual order entry).
- `GET /api/orders` — requires auth. Non-staff are forced to filter by their own id.
- `GET /api/orders/{id}` — requires auth. Non-staff get **404** for orders they don't
  own (no existence leak). Staff/Admin see all.
- `PATCH /api/orders/{id}/status` — Staff/Admin only (existing).
- `/api/admin/*`, `/api/import/*` review: admin/staff role required (existing).

## Configuration

```
Jwt__Secret                     # 32+ random chars — REQUIRED in prod (boot fails otherwise)
Hangfire__DashboardUser         # prod /hangfire Basic-auth user
Hangfire__DashboardPassword     # prod /hangfire Basic-auth password
ConnectionStrings__*            # must NOT contain the dev SA password in prod
```

Docker (`docker-compose.yml`) ships demo dashboard creds `admin / admin123` — **change
these** for any real deployment. Secrets come from environment variables (or Docker
secrets); never commit them. Rotation = update the env var + restart; JWT refresh
tokens are revocable server-side (RefreshTokens table) so sessions can be cut.

## How to verify

```bash
docker compose up -d

# Security headers present
curl -sI http://localhost/api/products | grep -i "x-frame-options\|x-content-type"

# Brute-force throttle (11th login within a minute → 429)
for i in $(seq 1 11); do \
  curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost/api/auth/login \
  -H 'content-type: application/json' -d '{"email":"x@x.com","password":"bad"}'; done

# Hangfire dashboard now prompts for Basic auth (admin / admin123)
curl -s -o /dev/null -w "%{http_code}\n" http://localhost/hangfire        # 401
curl -s -o /dev/null -w "%{http_code}\n" -u admin:admin123 http://localhost/hangfire  # 200

# Customer can't read another customer's order (404), nor list others' orders
```

## Out of scope (future)

- Per-account login lockout (rate limiting covers the common case).
- 2FA / email-OTP.
- Secrets vault (Azure Key Vault / HashiCorp Vault) — see Phase 6.
