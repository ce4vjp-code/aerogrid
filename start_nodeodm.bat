@echo off
echo Iniciando Motor de Procesamiento (NodeODM)...
docker run -d -p 3000:3000 opendronemap/nodeodm
echo.
echo Motor iniciado exitosamente. Ya puedes abrir PowerScan3D.
pause