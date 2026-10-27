@echo off
rem OAR Steel add-in: install / update. Put a shortcut to this file in each user's Startup folder
rem (Win+R, shell:startup) or run it once by double-clicking.
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install-OARSteel.ps1"
