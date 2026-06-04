# Frontend

React 18 + TypeScript + Vite. Tremor + Tailwind CSS for the admin BI UI. Root: `frontend/`.

## Routing (`src/App.tsx`)

Two layouts under React Router 6:
- **PublicLayout** (storefront): `/` Landing, `/shop`, `/shop/:id`, `/login`, `/register`; protected `/checkout`, `/my-orders` (wrapped in `ProtectedRoute`).
- **AppLayout** (admin, `requireRole="Staff"`): `/admin` Dashboard, `/admin/orders`, `/admin/orders/new`, `/admin/orders/:id`, `/admin/import`, `/admin/stress`.

`ProtectedRoute` redirects unauthenticated users to `/login` (stashing the origin in router state) and bounces non-Admin/Staff away from `/admin`.

## Contexts

- **AuthContext** (`contexts/AuthContext.tsx`): JWT session in localStorage (`ecom.auth`). Exposes `user`, `role`, `isAuthenticated`, `isAdmin`, `login`, `register`, `logout`. `getStoredAuth`/`setStoredAuth` are used by the axios layer (outside React). `loadSession` validates shape and wipes stale blobs.
- **CartContext** (`contexts/CartContext.tsx`): cart in localStorage (`ecom.cart`). `add`, `setQuantity`, `remove`, `clear`, computed `itemCount` + `totalValue`.

## API layer (`src/api/`)

- `client.ts`: axios instance. Request interceptor injects `Authorization: Bearer`. Response interceptor: treats backend **499 as a cancel** (→ CanceledError), and on **401** calls `/api/auth/refresh` once (coalesced) then retries the original request; on refresh failure wipes session + emits `auth:expired`. `isAbortError(e)` helper.
- `auth.ts`, `reports.ts`, `orders.ts`, `lookups.ts`, `imports.ts`: typed call wrappers. Base URL is empty (relative) → same-origin; Vite dev proxies `/api` + `/hub` to `:5193` (see `vite.config.ts`).

## Real-time (`hooks/useEtlNotifications.ts`)

SignalR client to `/hub/etl`. StrictMode-safe: tracks a `cancelled` flag, swallows `AbortError` during the start/stop race, stores the latest callback in a ref so the effect doesn't re-subscribe. Dashboard passes a callback that refetches reports on `etl-completed` → auto-refresh without F5.

## Data fetching pattern

Pages use `useEffect` + `AbortController`; on dependency change the previous request is aborted (cleanup) before a new one starts. Abort errors are swallowed via `isAbortError` (no fake error banner). Reports API accept an optional `AbortSignal`.

## Styling

`darkMode: 'class'` with `dark` set on `<html>` (index.html). `tailwind.config.js` defines Tremor color/shadow tokens. `index.css` `@layer components` overrides Tremor content colors for higher contrast on the dark background.

## Build / serve

- Dev: `npm run dev` (Vite, :5173, proxies to backend).
- Docker: `Dockerfile` builds with Vite then serves via nginx (`nginx.conf` reverse-proxies `/api`, `/hub` WebSocket upgrade, `/hangfire`, `/scalar`; SPA fallback to index.html).
- Azure single-deployment: the API serves the built SPA from `wwwroot` (`UseStaticFiles` + `MapFallbackToFile` guarded on `wwwroot/index.html`); frontend calls `/api` relative → same origin, no CORS.
