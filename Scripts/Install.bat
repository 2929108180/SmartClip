@echo off
:: SmartClip 安装启动器
:: 双击此文件即可开始安装

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
