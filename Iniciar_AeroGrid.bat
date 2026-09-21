@echo off
setlocal
title Iniciar AeroGrid
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Iniciar.ps1"
