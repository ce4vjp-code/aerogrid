@echo off
setlocal
title Instalador AeroGrid
cd /d "%~dp0"

:: Comprobar si se ejecuta con permisos de administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ============================================================
    echo       AEROGRID - SOLICITANDO PERMISOS DE ADMINISTRADOR
    echo ============================================================
    echo.
    echo Este instalador requiere privilegios elevados para copiar
    echo los archivos y configurar los accesos directos y Docker.
    echo.
    powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1"
if %errorlevel% neq 0 (
    echo.
    echo Hubo un detalle durante la instalacion. Revisa los mensajes arriba.
    pause
)
