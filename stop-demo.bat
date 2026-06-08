@echo off
REM Double-click to stop the demo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop-demo.ps1"
pause
