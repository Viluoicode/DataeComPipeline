# stop-demo.ps1 — stop everything start-demo.ps1 launched.
$ErrorActionPreference = 'Continue'

foreach ($port in 5193, 8090, 5173) {
    $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($conns) {
        $conns.OwningProcess | Sort-Object -Unique | ForEach-Object {
            Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Stopped service on :$port" -ForegroundColor Green
    }
}

Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "Stopped Cloudflare tunnel" -ForegroundColor Green
Write-Host "Demo stopped." -ForegroundColor Cyan
