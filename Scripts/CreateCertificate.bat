@echo off
:: Generate SmartClip Certificate
:: Double-click to run

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CreateCertificate.ps1"
pause
