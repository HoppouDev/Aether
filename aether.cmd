@echo off
rem Windows cmd.exe launcher: forwards to aether.ps1 (bypassing the script execution policy).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0aether.ps1" %*
exit /b %ERRORLEVEL%
