@echo off
powershell.exe -NoProfile -File "%~dp0scripts\Iniciar.ps1"
if errorlevel 1 pause
