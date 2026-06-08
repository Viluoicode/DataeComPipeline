# Deploy to a VPS (always-on, HTTPS) — step by step

The whole stack (SQL Server + API + frontend + AI analyst) runs on one small
Linux VPS via Docker Compose. Caddy gives automatic HTTPS. End result: a live
`https://your-domain` you can put on your CV.

> Why a VPS and not a free PaaS: SQL Server needs ~2 GB RAM and has no free
> managed tier, so free hosts (Render/Railway/Fly) end up costing money anyway.
> A small VPS is the cheapest *reliable* full-feature option (~$5–6/month).

> **Free alternative (no VPS):** double-click **`start-demo.bat`** to run the whole
> stack locally and expose it via a Cloudflare Quick Tunnel — a public `https://…
> .trycloudflare.com` URL. It's free but only live while your PC + the demo are
> running, and the URL changes each run. Good for live interview demos; use the VPS
> below for an always-on link you can put on a CV.

---

## 1. Get a VPS

Pick one (Ubuntu 22.04 or 24.04):

| Provider | Plan | RAM | ~Price |
|---|---|---|---|
| **Hetzner** | CX32 | 8 GB | ~€6/mo |
| **Contabo** | VPS S | 8 GB | ~$6/mo |
| DigitalOcean / Vultr | 4 GB droplet | 4 GB | ~$24/mo (pricier) |

**Get 8 GB.** SQL Server + two .NET builds need headroom; building the frontend
on 4 GB can OOM (rolldown is memory-hungry). 8 GB avoids all of that.

Note the server's **public IP**.

---

## 2. Point a domain at the VPS

Caddy needs a real domain to issue HTTPS (Let's Encrypt won't certify a raw IP).

- **Have a domain?** Add an **A record**: `ecom.yourdomain.com → <VPS IP>`.
- **Want it free?** Create a subdomain at [duckdns.org](https://www.duckdns.org)
  (e.g. `yourname.duckdns.org`) and point it at the VPS IP.

Wait a few minutes for DNS, then verify: `ping ecom.yourdomain.com` → VPS IP.

---

## 3. Install Docker on the VPS

```bash
ssh root@<VPS IP>
curl -fsSL https://get.docker.com | sh
docker --version          # confirm
```

### Add swap (safety net for the build on smaller boxes)
```bash
fallocate -l 4G /swapfile && chmod 600 /swapfile
mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

### Open the firewall (if ufw is on)
```bash
ufw allow 80 && ufw allow 443 && ufw allow OpenSSH && ufw --force enable
```

---

## 4. Clone, configure, launch

```bash
git clone https://github.com/Viluoicode/DataeComPipeline.git
cd DataeComPipeline

cp .env.example .env
nano .env          # set DOMAIN, SA_PASSWORD, JWT_SECRET (see below)
```

`.env` values:
- `DOMAIN` — the domain from step 2 (no http://).
- `SA_PASSWORD` — 8+ chars, upper+lower+digit+symbol.
- `JWT_SECRET` — 32+ random chars. Generate: `openssl rand -base64 48`.

Launch:
```bash
docker compose -f docker-compose.prod.yml up -d --build
```

First run takes ~5–10 min (builds .NET + frontend, pulls SQL Server, seeds DB,
runs the first ETL). Watch progress:
```bash
docker compose -f docker-compose.prod.yml logs -f api      # wait for "Now listening"
docker compose -f docker-compose.prod.yml ps               # all healthy/running
```

---

## 5. Open it

`https://your-domain` → storefront. Log in at `/admin` with the seeded admin
(`admin@ecom.com` / `admin123`). Trigger an ETL from **Stress Test** so the Gold
layer (and the **Ask Data** AI page) has data.

Caddy fetches the HTTPS cert automatically on first request — give it ~30 s.

---

## 6. Operate

```bash
# Update after pushing new code
git pull && docker compose -f docker-compose.prod.yml up -d --build

# Logs / status
docker compose -f docker-compose.prod.yml logs -f --tail=100
docker compose -f docker-compose.prod.yml ps

# Stop (keeps data) / wipe data
docker compose -f docker-compose.prod.yml down
docker compose -f docker-compose.prod.yml down -v        # also deletes the DB volume
```

---

## Security notes (do before sharing widely)

- **Change `admin123`** — the seeded demo password is public in this repo. Either
  change it after first login or lower the demo's privileges.
- **`/hangfire` is reachable** through the proxy. It's a job dashboard — fine for a
  demo, but consider blocking it in `Caddyfile` (`handle /hangfire* { respond 404 }`)
  or adding `basic_auth` if the site gets real traffic.
- **Never commit `.env`** (it's gitignored). Secrets live only on the server.
- `analyst_ro` (read-only on `gold`) and the Production JWT fail-fast are already
  enforced — see `docs/DECISIONS.md` → Production Roadmap.

## Cost / teardown
~$6/month while it runs. Pause anytime with `down`; destroy the VPS from the
provider console to stop billing entirely.
