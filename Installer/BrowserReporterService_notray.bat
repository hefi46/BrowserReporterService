@echo off
REM Browser Reporter Service - No Tray Icon Mode
REM This script launches the Browser Reporter Service without the system tray icon
REM Designed for Group Policy deployment

"%~dp0BrowserReporterService.exe" --no-tray
