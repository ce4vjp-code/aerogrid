@echo off
echo Empaquetando PowerScan3D Alpha 0.2.0...
dotnet publish PowerScan3D.App\PowerScan3D.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\Publish_Alpha_0.2.0
copy start_nodeodm.bat .\Publish_Alpha_0.2.0\
echo.
echo Empaquetado completado en la carpeta Publish_Alpha_0.2.0
pause