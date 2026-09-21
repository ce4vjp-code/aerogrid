# Iniciar.ps1 - Lanzador Inteligente de AeroGrid
$Host.UI.RawUI.WindowTitle = "Iniciando AeroGrid..."
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "                  INICIANDO AEROGRID                        " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$AppDir = $PSScriptRoot
$AppExe = Join-Path $AppDir "App\PowerScan3D.App.exe"
$DockerDesktopPath = "C:\Program Files\Docker\Docker\Docker Desktop.exe"

if (-not (Test-Path $AppExe)) {
    Write-Host "[ERROR] No se encontro el ejecutable en: $AppExe" -ForegroundColor Red
    Pause
    Exit 1
}

# 1. Comprobar si el motor de Docker esta respondiendo
Write-Host "`n[1/3] Verificando estado de Docker Desktop..." -ForegroundColor Yellow
$dockerReady = $false

try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -eq 0) {
        $dockerReady = $true
        Write-Host " -> Docker ya esta en ejecucion." -ForegroundColor Green
    }
} catch {
    $dockerReady = $false
}

if (-not $dockerReady) {
    if (Test-Path $DockerDesktopPath) {
        Write-Host " -> Docker no esta encendido. Iniciando Docker Desktop en segundo plano..." -ForegroundColor Cyan
        Start-Process -FilePath $DockerDesktopPath -WindowStyle Minimized
        
        $maxSeconds = 60
        $elapsed = 0
        Write-Host " -> Esperando que el motor de Docker responda (esto puede tomar hasta 45s)..." -NoNewline
        
        while ($elapsed -lt $maxSeconds) {
            Start-Sleep -Seconds 3
            $elapsed += 3
            Write-Host "." -NoNewline
            
            try {
                $null = docker info 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $dockerReady = $true
                    break
                }
            } catch {}
        }
        Write-Host ""
        
        if ($dockerReady) {
            Write-Host " -> Motor de Docker listo." -ForegroundColor Green
        } else {
            Write-Host " -> [AVISO] Docker tardo en responder. La app abrira pero el analisis IA podria demorar unos segundos en estar listo." -ForegroundColor DarkYellow
        }
    } else {
        Write-Host " -> [AVISO] No se encontro Docker Desktop en la ruta estandar." -ForegroundColor DarkYellow
        Write-Host "    AeroGrid abrira. Para usar fotogrametria e IA, asegurate de tener Docker Desktop." -ForegroundColor Gray
    }
}

# 2. Levantar contenedores si Docker esta listo
if ($dockerReady) {
    Write-Host "`n[2/3] Verificando contenedores de procesamiento (NodeODM e IA)..." -ForegroundColor Yellow
    Push-Location $AppDir
    try {
        docker compose up -d
        if ($LASTEXITCODE -eq 0) {
            Write-Host " -> Contenedores activos (NodeODM en puerto 3000, IA en puerto 8000)." -ForegroundColor Green
        } else {
            Write-Host " -> [AVISO] Hubo un detalle al iniciar contenedores con docker compose." -ForegroundColor DarkYellow
        }
    } catch {
        Write-Host " -> Error al ejecutar docker compose: $_" -ForegroundColor Red
    } finally {
        Pop-Location
    }
} else {
    Write-Host "`n[2/3] Omitiendo verificacion de contenedores (Docker no listo aun)." -ForegroundColor Gray
}

# 3. Lanzar la aplicacion AeroGrid
Write-Host "`n[3/3] Abriendo AeroGrid..." -ForegroundColor Green
Start-Process -FilePath $AppExe

Start-Sleep -Seconds 2
Exit 0
