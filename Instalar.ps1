# Instalar.ps1 - Instalador Maestro de AeroGrid
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = "Instalador de AeroGrid Alpha 0.2.0"

function Print-Banner {
    Clear-Host
    Write-Host "========================================================================" -ForegroundColor Cyan
    Write-Host "               AEROGRID (ALPHA 0.2.0) - INSTALADOR MAESTRO               " -ForegroundColor Cyan
    Write-Host "   Inspeccion Aerea, Fotogrametria y Deteccion de Vegetacion con IA   " -ForegroundColor DarkCyan
    Write-Host "========================================================================" -ForegroundColor Cyan
    Write-Host ""
}

Print-Banner

# 1. Comprobar permisos de Administrador
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[ERROR] Este instalador requiere permisos de Administrador." -ForegroundColor Red
    Write-Host "Por favor ejecuta 'Instalar_AeroGrid.bat' como Administrador." -ForegroundColor Yellow
    Pause
    Exit 1
}

# 2. Definir ruta de instalación
$DefaultInstallDir = "C:\AeroGrid"
Write-Host "Paso 1: Directorio de Instalacion" -ForegroundColor Yellow
Write-Host "Ruta predeterminada: $DefaultInstallDir" -ForegroundColor Gray
$userPath = Read-Host "Presiona [ENTER] para aceptar o escribe otra ruta"
$InstallDir = if ([string]::IsNullOrWhiteSpace($userPath)) { $DefaultInstallDir } else { $userPath.Trim() }

Write-Host "`nInstalando en: $InstallDir" -ForegroundColor Green

# 3. Copiar archivos del programa
Write-Host "`n[1/5] Copiando archivos del sistema AeroGrid..." -ForegroundColor Yellow
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$SourceDir = $PSScriptRoot

# Copiar App
Write-Host " -> Copiando aplicacion de escritorio (C# / WebView2)..." -ForegroundColor Gray
$srcApp = Join-Path $SourceDir "App"
$dstApp = Join-Path $InstallDir "App"
if (Test-Path $srcApp) {
    Copy-Item -Path $srcApp -Destination $InstallDir -Recurse -Force
    # Limpiar cache temporal si vino incluida
    $cache = Join-Path $dstApp "PowerScan3D.App.exe.WebView2"
    if (Test-Path $cache) { Remove-Item -Path $cache -Recurse -Force -ErrorAction SilentlyContinue }
} else {
    Write-Host "[ERROR] No se encontro la carpeta 'App' en $SourceDir" -ForegroundColor Red
    Pause
    Exit 1
}

# Copiar Motor IA
Write-Host " -> Copiando motor de Inteligencia Artificial (Python/FastAPI)..." -ForegroundColor Gray
$srcAI = Join-Path $SourceDir "PowerScan3D.AI"
if (Test-Path $srcAI) {
    Copy-Item -Path $srcAI -Destination $InstallDir -Recurse -Force
}

# Copiar Pesos de la Red Neuronal (NEON.pt)
$srcNeon = Join-Path $SourceDir "NEON.pt"
if (Test-Path $srcNeon) {
    Write-Host " -> Copiando pesos del modelo neuronal (NEON.pt)..." -ForegroundColor Gray
    Copy-Item -Path $srcNeon -Destination $InstallDir -Force
}

# Copiar docker-compose y scripts auxiliares
Write-Host " -> Copiando orquestador y scripts de ejecucion..." -ForegroundColor Gray
$filesToCopy = @("docker-compose.yml", "Iniciar_AeroGrid.bat", "Iniciar.ps1", "Detener_AeroGrid.bat")
foreach ($f in $filesToCopy) {
    $srcF = Join-Path $SourceDir $f
    if (Test-Path $srcF) {
        Copy-Item -Path $srcF -Destination $InstallDir -Force
    }
}

Write-Host " Archivos copiados correctamente." -ForegroundColor Green

# 4. Verificación e Instalación de Docker Desktop
Write-Host "`n[2/5] Verificando Docker Desktop..." -ForegroundColor Yellow
$DockerInstalled = $false
$DockerDesktopExe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"

if (Get-Command "docker" -ErrorAction SilentlyContinue) {
    $DockerInstalled = $true
} elseif (Test-Path $DockerDesktopExe) {
    $DockerInstalled = $true
}

if ($DockerInstalled) {
    Write-Host " Docker Desktop ya esta instalado en este equipo." -ForegroundColor Green
} else {
    Write-Host " Docker Desktop no se encontro instalado." -ForegroundColor Yellow
    Write-Host "   AeroGrid necesita Docker para ejecutar NodeODM (Fotogrametria) y la IA de arboles." -ForegroundColor Gray
    $installDocker = Read-Host " Deseas descargar e instalar Docker Desktop automaticamente? [S/n]"
    if ($installDocker -ne 'n' -and $installDocker -ne 'N') {
        Write-Host "`n -> Intentando instalacion automatica de Docker Desktop..." -ForegroundColor Cyan
        
        $wingetInstalled = (Get-Command "winget" -ErrorAction SilentlyContinue) -ne $null
        $installedViaWinget = $false
        
        if ($wingetInstalled) {
            Write-Host " -> Instalando mediante Windows Package Manager (winget)..." -ForegroundColor Gray
            try {
                winget install -e --id Docker.DockerDesktop --accept-source-agreements --accept-package-agreements --silent
                if ($LASTEXITCODE -eq 0 -or (Test-Path $DockerDesktopExe)) {
                    $installedViaWinget = $true
                    $DockerInstalled = $true
                    Write-Host " Docker Desktop instalado exitosamente via winget." -ForegroundColor Green
                }
            } catch {}
        }
        
        if (-not $installedViaWinget) {
            Write-Host " -> Descargando instalador oficial de Docker Desktop..." -ForegroundColor Cyan
            $installerUrl = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe"
            $tempInstaller = "$env:TEMP\DockerDesktopInstaller.exe"
            
            try {
                Invoke-WebRequest -Uri $installerUrl -OutFile $tempInstaller -UseBasicParsing
                Write-Host " -> Ejecutando instalador oficial de Docker (espera a que finalice)..." -ForegroundColor Cyan
                $proc = Start-Process -FilePath $tempInstaller -ArgumentList "install --quiet --accept-license" -Wait -PassThru
                if ($proc.ExitCode -eq 0 -or (Test-Path $DockerDesktopExe)) {
                    $DockerInstalled = $true
                    Write-Host " Docker Desktop instalado exitosamente." -ForegroundColor Green
                }
            } catch {
                Write-Host "[AVISO] No se pudo descargar Docker automaticamente: $_" -ForegroundColor DarkYellow
                Write-Host "Puedes descargarlo manualmente desde: https://www.docker.com/products/docker-desktop/" -ForegroundColor Gray
            }
        }
    }
}

# 5. Iniciar Docker y Configurar Contenedores
Write-Host "`n[3/5] Configurando motores de procesamiento y contenedores..." -ForegroundColor Yellow
$dockerUp = $false

try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -eq 0) { $dockerUp = $true }
} catch {}

if (-not $dockerUp -and (Test-Path $DockerDesktopExe)) {
    Write-Host " -> Iniciando Docker Desktop en segundo plano..." -ForegroundColor Cyan
    Start-Process -FilePath $DockerDesktopExe -WindowStyle Minimized
    $waitLimit = 60
    $waited = 0
    Write-Host " -> Esperando respuesta del motor Docker..." -NoNewline
    while ($waited -lt $waitLimit) {
        Start-Sleep -Seconds 3
        $waited += 3
        Write-Host "." -NoNewline
        try {
            $null = docker info 2>&1
            if ($LASTEXITCODE -eq 0) {
                $dockerUp = $true
                break
            }
        } catch {}
    }
    Write-Host ""
}

if ($dockerUp) {
    Write-Host " Docker Engine activo y listo." -ForegroundColor Green
    
    # Comprobar si hay paquetes offline en la carpeta images/
    $imagesDir = Join-Path $SourceDir "images"
    if (Test-Path $imagesDir) {
        $tarFiles = Get-ChildItem -Path $imagesDir -Filter "*.tar"
        if ($tarFiles.Count -gt 0) {
            Write-Host " -> Se detectaron imagenes offline en la carpeta 'images'. Cargando en Docker..." -ForegroundColor Cyan
            foreach ($tar in $tarFiles) {
                Write-Host "    Cargando $($tar.Name)..." -ForegroundColor Gray
                docker load -i $tar.FullName
            }
            Write-Host " Imagenes offline cargadas exitosamente." -ForegroundColor Green
        }
    }
    
    # Construir / Descargar contenedores con docker compose
    Write-Host " -> Desplegando servicios con docker compose (NodeODM + IA)..." -ForegroundColor Cyan
    Push-Location $InstallDir
    try {
        docker compose up -d --build
        if ($LASTEXITCODE -eq 0) {
            Write-Host " Contenedores NodeODM e IA configurados y listos." -ForegroundColor Green
        } else {
            Write-Host " [AVISO] docker compose devolvio un codigo distinto de 0. Se completara en el primer inicio." -ForegroundColor DarkYellow
        }
    } catch {
        Write-Host " [AVISO] No se pudo ejecutar docker compose ahora: $_" -ForegroundColor DarkYellow
    } finally {
        Pop-Location
    }
} else {
    Write-Host " [AVISO] Docker Desktop aun no esta encendido o requiere reinicio de sesion." -ForegroundColor DarkYellow
    Write-Host " Los contenedores se descargaran/iniciaran automaticamente la primera vez que abras AeroGrid." -ForegroundColor Gray
}

# 6. Crear Accesos Directos
Write-Host "`n[4/5] Creando accesos directos..." -ForegroundColor Yellow
$DesktopPath = [Environment]::GetFolderPath("Desktop")
$ShortcutPath = Join-Path $DesktopPath "AeroGrid.lnk"
$TargetBat = Join-Path $InstallDir "Iniciar_AeroGrid.bat"
$IconExe = Join-Path $InstallDir "App\PowerScan3D.App.exe"

try {
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $TargetBat
    $Shortcut.WorkingDirectory = $InstallDir
    $Shortcut.IconLocation = "$IconExe, 0"
    $Shortcut.Description = "AeroGrid - Sistema de Analisis Aereo y Vegetacion"
    $Shortcut.Save()
    Write-Host " Acceso directo creado en el Escritorio: AeroGrid.lnk" -ForegroundColor Green
    
    # Crear tambien en el Menu Inicio
    $StartMenu = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
    if (Test-Path $StartMenu) {
        $SmShortcut = $WshShell.CreateShortcut((Join-Path $StartMenu "AeroGrid.lnk"))
        $SmShortcut.TargetPath = $TargetBat
        $SmShortcut.WorkingDirectory = $InstallDir
        $SmShortcut.IconLocation = "$IconExe, 0"
        $SmShortcut.Description = "AeroGrid"
        $SmShortcut.Save()
    }
} catch {
    Write-Host " [AVISO] No se pudo crear el acceso directo COM: $_" -ForegroundColor DarkYellow
}

# 7. Crear Script de Desinstalación
Write-Host "`n[5/5] Creando desinstalador..." -ForegroundColor Yellow
$UninstallScript = @"
@echo off
setlocal
title Desinstalar AeroGrid
echo ============================================================
echo               DESINSTALADOR DE AEROGRID
echo ============================================================
echo.
set /p resp="Estas seguro de desinstalar AeroGrid de este equipo? (S/N): "
if /i not "%resp%"=="S" exit /b 0

echo.
echo Deteniendo contenedores de Docker...
cd /d "%~dp0"
docker compose down >nul 2>&1

echo Eliminando accesos directos...
del /f /q "%USERPROFILE%\Desktop\AeroGrid.lnk" >nul 2>&1
del /f /q "%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\AeroGrid.lnk" >nul 2>&1

echo.
echo ============================================================
echo Para finalizar, elimina manualmente la carpeta:
echo %~dp0
echo ============================================================
pause
"@

Set-Content -Path (Join-Path $InstallDir "Desinstalar_AeroGrid.bat") -Value $UninstallScript -Encoding ASCII
Write-Host " Desinstalador creado en $InstallDir\Desinstalar_AeroGrid.bat" -ForegroundColor Green

# 8. Finalización
Write-Host "`n========================================================================" -ForegroundColor Cyan
Write-Host "                  INSTALACION COMPLETADA CON EXITO!                     " -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "AeroGrid se ha instalado en: $InstallDir" -ForegroundColor White
Write-Host "Puedes iniciarlo desde el icono 'AeroGrid' que quedo en tu Escritorio." -ForegroundColor White
Write-Host ""

$startNow = Read-Host "Deseas iniciar AeroGrid en este momento? [S/n]"
if ($startNow -ne 'n' -and $startNow -ne 'N') {
    Start-Process -FilePath $TargetBat
}

Exit 0
