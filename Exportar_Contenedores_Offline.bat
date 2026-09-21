@echo off
setlocal
title Exportar Contenedores AeroGrid
cd /d "%~dp0"
echo ============================================================
echo   AEROGRID - EXPORTADOR DE CONTENEDORES PARA USO OFFLINE
echo ============================================================
echo.
echo Este script guardara las imagenes Docker en archivos .tar
echo dentro de la carpeta 'images', para que puedas llevarlas
echo en un pendrive al PC de pruebas sin requerir internet.
echo.
if not exist "images" mkdir "images"

echo Verificando si Docker esta en ejecucion...
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Docker Desktop no esta iniciado.
    echo Inicia Docker Desktop primero y vuelve a ejecutar este script.
    echo.
    pause
    exit /b 1
)

echo.
echo [1/2] Exportando imagen de NodeODM (opendronemap/nodeodm:latest)...
echo Nota: Este archivo pesa aprox. 4-5 GB. Puede tardar varios minutos.
docker save -o "images\nodeodm.tar" opendronemap/nodeodm:latest
if %errorlevel% equ 0 (
    echo  -> NodeODM exportado exitosamente a images\nodeodm.tar
) else (
    echo  -> [AVISO] No se pudo exportar opendronemap/nodeodm. Asegurate de haber hecho 'docker pull opendronemap/nodeodm' previamente.
)

echo.
echo [2/2] Exportando imagen de IA (powerscan_ai)...
docker compose build ai-engine
docker save -o "images\powerscan_ai.tar" aerogrid-ai-engine powerscan_ai >nul 2>&1
if %errorlevel% neq 0 (
    docker save -o "images\powerscan_ai.tar" powerscan_ai
)
if %errorlevel% equ 0 (
    echo  -> Motor de IA exportado exitosamente a images\powerscan_ai.tar
) else (
    echo  -> [AVISO] No se encontro la imagen powerscan_ai.
)

echo.
echo ============================================================
echo Proceso finalizado.
echo Si copias la carpeta 'images' junto con el instalador,
echo el instalador detectara las imagenes automaticamente y no
echo descargara nada de internet en el nuevo computador.
echo ============================================================
echo.
pause
