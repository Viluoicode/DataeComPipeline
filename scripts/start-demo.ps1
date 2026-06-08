# =============================================================================
#  start-demo.ps1 — bring the whole demo up with one click.
#  Starts: API (:5193) + AI Analyst (:8090) + Frontend (:5173) + Cloudflare
#  Quick Tunnel, then prints the public https URL (also copied to clipboard
#  and saved to demo-url.txt).
#
#  Run via start-demo.bat (double-click). Stop via stop-demo.bat.
#  Note: the quick-tunnel URL changes every run (free tier). For a permanent
#  URL, deploy to a VPS with docker-compose.prod.yml (see docs/DEPLOY_VPS.md).
# =============================================================================
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Find-Cloudflared {
    $cands = @(
        "C:\Program Files (x86)\cloudflared\cloudflared.exe",
        "C:\Program Files\cloudflared\cloudflared.exe",
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links\cloudflared.exe"
    )
    foreach ($p in $cands) { if (Test-Path $p) { return $p } }
    $c = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}

function Wait-Url($url, $name, $tries = 40) {
    for ($i = 0; $i -lt $tries; $i++) {
        try { Invoke-WebRequest -Uri $url -TimeoutSec 4 -UseBasicParsing | Out-Null
              Write-Host "  $name is UP" -ForegroundColor Green; return $true }
        catch { Start-Sleep 3 }
    }
    Write-Host "  $name not up yet (continuing anyway)" -ForegroundColor Red; return $false
}

Write-Host "=== ECommerPipeline - Start Demo ===" -ForegroundColor Cyan

# 0. SQL Server engine
$sql = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
if ($sql -and $sql.Status -ne 'Running') {
    Write-Host "Starting SQL Server..." -ForegroundColor Yellow
    try { Start-Service MSSQLSERVER } catch { Write-Host "  Could not start SQL (need admin?). Start it manually." -ForegroundColor Red }
}

# 0b. Ensure the read-only analyst_ro principal exists (idempotent)
try { sqlcmd -S localhost -E -b -i "ai-analyst\db\create_readonly_user.ecommerce.sql" 2>$null | Out-Null
      Write-Host "  analyst_ro ready" -ForegroundColor DarkGray } catch { }

# 1. API
Write-Host "Launching API (:5193)..." -ForegroundColor Yellow
$apiCmd = "`$host.UI.RawUI.WindowTitle='ECOM API :5193'; `$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:Seed__CustomerCount='500'; `$env:Seed__ProductCount='100'; `$env:Seed__OrderCount='5000'; Set-Location '$root'; dotnet run --project src/ECommerPipeline.Api --urls http://localhost:5193"
Start-Process powershell -ArgumentList '-NoExit', '-Command', $apiCmd

# 2. AI Analyst
Write-Host "Launching AI Analyst (:8090)..." -ForegroundColor Yellow
$anaCmd = "`$host.UI.RawUI.WindowTitle='ECOM Analyst :8090'; `$env:ConnectionStrings__Analyst='Server=localhost;Database=ECommerPipeline_Olap;User Id=analyst_ro;Password=Readonly#Analyst1;TrustServerCertificate=True;Encrypt=False'; `$env:Analyst__SchemaConfigPath='config/schema.ecommerce.json'; `$env:Analyst__Provider='Offline'; Set-Location '$root\ai-analyst'; dotnet run --project src/Analyst.Api --urls http://localhost:8090"
Start-Process powershell -ArgumentList '-NoExit', '-Command', $anaCmd

# 3. Frontend deps + dist (first run only)
if (-not (Test-Path "$root\frontend\node_modules")) {
    Write-Host "Installing frontend deps (first run)..." -ForegroundColor Yellow
    Push-Location "$root\frontend"; npm ci --legacy-peer-deps; Pop-Location
}
if (-not (Test-Path "$root\frontend\dist\index.html")) {
    Write-Host "Building frontend (first run)..." -ForegroundColor Yellow
    Push-Location "$root\frontend"; npm run build; Pop-Location
}
Write-Host "Launching Frontend (:5173)..." -ForegroundColor Yellow
$feCmd = "`$host.UI.RawUI.WindowTitle='ECOM Frontend :5173'; Set-Location '$root\frontend'; npm run preview"
Start-Process powershell -ArgumentList '-NoExit', '-Command', $feCmd

# 4. Wait for services
Write-Host "Waiting for services to come up..." -ForegroundColor Cyan
Wait-Url "http://localhost:5193/health" "API"
Wait-Url "http://localhost:5173/" "Frontend"

# 5. Cloudflare tunnel
$cf = Find-Cloudflared
if (-not $cf) {
    Write-Host "cloudflared NOT found. Install it:  winget install Cloudflare.cloudflared" -ForegroundColor Red
    Read-Host "Press Enter to exit"; exit 1
}
$cfOut = "$env:TEMP\ecom-cf-out.log"; $cfErr = "$env:TEMP\ecom-cf-err.log"
Remove-Item $cfOut, $cfErr -Force -ErrorAction SilentlyContinue
Write-Host "Opening Cloudflare tunnel..." -ForegroundColor Yellow
Start-Process $cf -ArgumentList 'tunnel', '--url', 'http://localhost:5173' `
    -RedirectStandardOutput $cfOut -RedirectStandardError $cfErr -WindowStyle Hidden

# 6. Extract the public URL
$url = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep 2
    foreach ($f in @($cfErr, $cfOut)) {
        if (Test-Path $f) {
            $m = Select-String -Path $f -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($m) { $url = $m.Matches[0].Value; break }
        }
    }
    if ($url) { break }
}

Write-Host ""
if ($url) {
    Set-Content -Path "$root\demo-url.txt" -Value $url
    try { Set-Clipboard -Value $url } catch { }
    Write-Host "====================================================" -ForegroundColor Green
    Write-Host "  DEMO LIVE:  $url" -ForegroundColor Green
    Write-Host "  (copied to clipboard + saved to demo-url.txt)" -ForegroundColor DarkGray
    Write-Host "  Login: admin@ecom.com / admin123" -ForegroundColor Green
    Write-Host "====================================================" -ForegroundColor Green
}
else {
    Write-Host "Could not auto-detect the URL. Open this file to find it:" -ForegroundColor Yellow
    Write-Host "  $cfErr" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "To STOP the demo: run stop-demo.bat (or close the ECOM ... windows)." -ForegroundColor Cyan
Read-Host "Press Enter to close this window (the demo keeps running)"
