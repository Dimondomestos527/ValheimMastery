@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Mastery.ps1" %*
if errorlevel 1 (echo Update failed.)
pause
