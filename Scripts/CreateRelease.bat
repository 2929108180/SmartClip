@echo off
:: Build and Package SmartClip Release
:: Double-click to run

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CreateRelease.ps1" -Version "1.0.1"
pause
