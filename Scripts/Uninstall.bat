@echo off
:: SmartClip 卸载启动器
:: 双击此文件即可卸载

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall
