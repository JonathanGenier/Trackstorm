@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-eos-multiplayer.ps1" -Executable "%~dp0Trackstorm_0.0.0.5.exe"
pause
