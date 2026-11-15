@echo off
rem OAR Steel add-in: install. Double-click once per user with Excel closed. After that the add-in updates
rem itself every time Excel starts (the OAR Steel Loader fetches the approved version from the office share).
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install-OARSteel.ps1"
