# Authentication

JWT-based, stateless access tokens + DB-stored refresh tokens. Files: `Infrastructure/Auth/{JwtTokenService, AuthService, JwtOptions}.cs`, `Application/Auth/`, endpoints in `Api/Program.cs`.

## Tokens

- **Access token (JWT):** HMAC-SHA256 signed, ~60 min (`Jwt:AccessTokenMinutes`). Claims: `sub` (user id), `email`, `role` (Customer/Staff/Admin), `jti`. NOT encrypted — never put secrets in the payload.
- **Refresh token:** 64 random bytes (base64), ~7 days, stored in `RefreshTokens` table so it can be revoked (JWT alone cannot). Rotated on use: old revoked + `ReplacedByToken` chain for audit.

`Jwt:Secret` must be ≥ 32 chars (256 bits); `JwtTokenService` ctor throws otherwise. Set via env in production.

## Flows (AuthService)

- **Register:** normalise email, reject duplicates, `BCrypt.HashPassword(workFactor: 11)`, save, issue tokens.
- **Login:** lookup by email, `BCrypt.Verify`. Identical error for missing-email vs wrong-password (no user enumeration). Update `LastLoginAt`, issue tokens.
- **Refresh:** validate stored refresh token is active → rotate (revoke old, insert new) + issue new access token.
- **Logout:** revoke the refresh token.

## API endpoints

`POST /api/auth/{register,login,refresh,logout}`, `GET /api/auth/me` (`[Authorize]`). Login/refresh failures → 401. All `/api/admin/*` endpoints require role Admin or Staff (`RequireAuthorization(p => p.RequireRole("Admin","Staff"))`).

## Middleware wiring (Program.cs)

`AddAuthentication(JwtBearer).AddJwtBearer(...)` with `TokenValidationParameters` (validate issuer/audience/lifetime/signing key, `ClockSkew = 1 min`). `app.UseAuthentication()` before `app.UseAuthorization()`.

## SignalR auth

Browsers can't set the `Authorization` header on the WebSocket upgrade, so the token is passed via query string. `JwtBearerEvents.OnMessageReceived` reads `access_token` from the query when the path starts with `/hub` and assigns it to `ctx.Token`.

## Frontend (see frontend.md)

`AuthContext` stores `{ accessToken, refreshToken, accessTokenExpiresAt, user }` in localStorage. `loadSession()` validates the shape and clears stale/malformed blobs (guards against old mock-auth format crashing on `user.role`). The axios interceptor injects the Bearer header and auto-refreshes once on 401.

## Seeded accounts

`DatabaseInitializer.SeedAsync` creates `admin@ecom.com` / `admin123` (Role=Admin) and `demo@ecom.com` / `demo123` (Role=Customer) before the bulk Bogus data.
