@echo off
rem Downloads and verifies the Acrux UI SDK: see update-acrux.ps1. usage: update-acrux.bat [version]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-acrux.ps1" %*
exit /b %ERRORLEVEL%
