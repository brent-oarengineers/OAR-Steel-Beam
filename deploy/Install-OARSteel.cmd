@echo off
rem OAR Steel: runs the shared OAR Loader installer (kept for old Startup shortcuts). See DEPLOYMENT.md.
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install-OARSteel.ps1"
