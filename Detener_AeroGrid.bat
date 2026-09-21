@echo off
setlocal
title Detener AeroGrid
cd /d "%~dp0"
echo ============================================================
echo   DETENIENDO CONTENEDORES DE AEROGRID (NodeODM e IA)
echo ============================================================
echo.
docker compose down
echo.
echo Contenedores detenidos correctamente. Memoria RAM y GPU liberadas.
timeout /t 3
