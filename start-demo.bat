@echo off
REM Double-click to start the whole demo (API + Analyst + Frontend + Cloudflare tunnel).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-demo.ps1"
