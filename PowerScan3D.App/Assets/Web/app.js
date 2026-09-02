/**
 * PowerScan 3D - Edición C# .NET 10
 * Puente bidireccional WebView2 <-> NetTopologySuite, LibTiff, QuestPDF y WebODM
 */

const state = {
    map: null,
    baseLayers: {},
    currentLayer: 'orthophoto',
    isBaseLayerActive: true,
    isCorridorVisible: true,
    isKmzVisible: true,
    corridorWidthM: 20.0,
    currentMission: null,
    analyzedTrees: [],
    currentFilter: 'all',
    activeKMZGeoJSON: null,
    activeKMZFileName: "LAT_220kV_Muestra.kmz",
    geotiffOverlay: null,
    geotiffBounds: null,
    geotiffRawDataUrl: null,
    geotiffDebugInfo: "",
    selectedDroneFolderPath: "",
    activeDroneShots: [],
    layers: {
        corridor: null,
        kmz: null,
        powerline: null,
        towers: null,
        trees: null,
        droneFlight: null
    }
};

document.addEventListener('DOMContentLoaded', () => {
    initMap();
    initUIControls();
    initCSharpBridge();
});

/**
 * Inicializar Puente Bidireccional con C# (.NET)
 */
function initCSharpBridge() {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => {
            const msg = event.data;
            if (msg && msg.action) {
                handleCSharpMessage(msg.action, msg.payload);
            }
        });

        sendToCSharp("init");
    } else {
        console.warn("WebView2 no detectado.");
    }
}

function sendToCSharp(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    }
}

function handleCSharpMessage(action, payload) {
    switch (action) {
        case "init_response":
            state.currentMission = payload.mission;
            state.analyzedTrees = payload.trees || [];
            state.corridorWidthM = payload.corridor_width_m || 20.0;
            renderCorridorPolygon(payload.corridor_polygon);
            renderTreesOnMap();
            renderTreeTable();
            updateMetricsAndUI();
            if (payload.library_catalog) {
                updateLibraryUI(payload.library_catalog);
            }
            break;

        case "library_catalog_updated":
            updateLibraryUI(payload);
            break;

        case "corridor_updated":
            state.analyzedTrees = payload.trees;
            state.corridorWidthM = payload.corridor_width_m;
            renderCorridorPolygon(payload.corridor_polygon);
            renderTreesOnMap();
            renderTreeTable();
            updateMetricsAndUI();
            break;

        case "kmz_loaded":
            renderKMZOnMap(payload.geojson, payload.filename);
            if (payload.corridor_polygon) {
                renderCorridorPolygon(payload.corridor_polygon);
            }
            if (payload.trees) {
                state.analyzedTrees = payload.trees;
                renderTreesOnMap();
                renderTreeTable();
                updateMetricsAndUI();
            }
            showToast(`KMZ cargado y servidumbre de ${payload.corridor_width_m || 20}m proyectada`);
            break;

        case "geotiff_loaded":
            handleGeoTiffLoaded(payload);
            // Auto-update DSM/DTM labels if they were auto-detected
            if (payload.has_dsm) updateElevationLabel('dsm', payload.dsm_filename || 'Auto-detectado');
            if (payload.has_dtm) updateElevationLabel('dtm', payload.dtm_filename || 'Auto-detectado');
            break;

        case "dsm_loaded":
            updateElevationLabel('dsm', payload.filename);
            showToast(`DSM cargado: ${payload.filename}`);
            break;

        case "dtm_loaded":
            updateElevationLabel('dtm', payload.filename);
            showToast(`DTM cargado: ${payload.filename}`);
            break;

        case "drone_flight_loaded":
            handleDroneFlightLoaded(payload);
            break;

        case "drone_photo_preview_ready":
            handleDronePhotoPreviewReady(payload);
            break;

        case "photogrammetry_progress":
            handlePhotogrammetryProgress(payload);
            break;

        case "photogrammetry_completed":
            handlePhotogrammetryCompleted(payload);
            break;

        case "refresh_trees":
            state.analyzedTrees = payload.trees;
            renderTreesOnMap();
            renderTreeTable();
            updateMetricsAndUI();
            break;

        case "real_analysis_completed":
            state.analyzedTrees = payload.trees;
            state.corridorWidthM = payload.corridor_width_m;
            if (payload.corridor_polygon) {
                renderCorridorPolygon(payload.corridor_polygon);
            }
            renderTreesOnMap();
            renderTreeTable();
            updateMetricsAndUI();
            let msg = `Análisis completado: ${payload.count} árboles identificados`;
            if (payload.ortho_area_ha) msg += ` | Área: ${payload.ortho_area_ha} ha`;
            if (payload.ortho_gsd_cm) msg += ` | GSD: ${payload.ortho_gsd_cm} cm/px`;
            showToast(msg);
            break;

        case "workspace_cleared":
            handleWorkspaceCleared();
            break;

        case "toast":
            showToast(payload.message);
            break;

        case "error":
            showToast(`Error: ${payload.message}`);
            break;
    }
}

/**
 * Actualiza el label de estado de DSM o DTM en la UI.
 * @param {'dsm'|'dtm'} type
 * @param {string} filename
 */
function updateElevationLabel(type, filename) {
    const labelId = type === 'dsm' ? 'dsmStatusLabel' : 'dtmStatusLabel';
    const label = document.getElementById(labelId);
    if (!label) return;
    const shortName = filename.length > 22 ? filename.substring(0, 20) + '…' : filename;
    label.title = filename;
    label.innerHTML = `<i class="fa-solid fa-circle-check"></i> ${shortName}`;
    label.style.opacity = '1';
}

/**
 * Limpieza completa del espacio de trabajo
 */
function handleWorkspaceCleared() {
    if (state.geotiffOverlay) {
        state.map.removeLayer(state.geotiffOverlay);
        state.geotiffOverlay = null;
    }
    state.geotiffBounds = null;
    state.geotiffRawDataUrl = null;
    state.geotiffDebugInfo = "";

    if (state.gpsSearchMarker) {
        state.map.removeLayer(state.gpsSearchMarker);
        state.gpsSearchMarker = null;
    }
    const btnClearGpsMarker = document.getElementById('btnClearGpsMarker');
    if (btnClearGpsMarker) btnClearGpsMarker.style.display = 'none';

    state.layers.corridor.clearLayers();
    state.layers.kmz.clearLayers();
    state.layers.powerline.clearLayers();
    state.layers.towers.clearLayers();
    state.layers.trees.clearLayers();
    state.layers.droneFlight.clearLayers();

    state.analyzedTrees = [];
    state.activeKMZGeoJSON = null;
    state.activeDroneShots = [];

    const geotiffCard = document.getElementById('geotiffInfoCard');
    if (geotiffCard) geotiffCard.style.display = 'none';

    const flightCard = document.getElementById('flightInfoCard');
    if (flightCard) flightCard.style.display = 'none';

    const kmzFileName = document.getElementById('kmzFileName');
    if (kmzFileName) kmzFileName.textContent = "Ningún KMZ cargado";

    const kmzFeatureCount = document.getElementById('kmzFeatureCount');
    if (kmzFeatureCount) kmzFeatureCount.textContent = "0";

    renderTreeTable();
    updateMetricsAndUI();

    showToast("Espacio de trabajo limpiado: Foto y trazado eliminados");
}

/**
 * Manejo de Ortofoto GeoTIFF cargada desde C#
 */
function handleGeoTiffLoaded(data) {
    const card = document.getElementById('geotiffInfoCard');
    const fileName = document.getElementById('geotiffFileName');
    const dimensions = document.getElementById('geotiffDimensions');
    const crs = document.getElementById('geotiffCrs');
    const gsdBadge = document.getElementById('geotiffGsdBadge');

    if (card) card.style.display = 'flex';
    if (fileName) fileName.textContent = data.filename;
    if (dimensions) dimensions.textContent = `${data.width} x ${data.height} px`;
    if (crs) crs.textContent = data.crs || (data.epsg ? `EPSG:${data.epsg}` : "Web Mercator");
    if (gsdBadge) gsdBadge.textContent = `GSD: ${data.gsd_cm} cm/px`;

    state.geotiffDebugInfo = data.debug_info || "No hay información adicional.";

    let minLat = data.minLat;
    let maxLat = data.maxLat;
    let minLon = data.minLon;
    let maxLon = data.maxLon;

    if (isNaN(minLat) || isNaN(minLon) || Math.abs(minLat) > 85 || Math.abs(minLon) > 180) {
        if (state.activeKMZGeoJSON) {
            const layer = L.geoJSON(state.activeKMZGeoJSON);
            const b = layer.getBounds();
            minLat = b.getSouth(); maxLat = b.getNorth();
            minLon = b.getWest(); maxLon = b.getEast();
        } else {
            minLat = -37.108; maxLat = -37.095; minLon = -72.555; maxLon = -72.535;
        }
    }

    const southWest = [minLat, minLon];
    const northEast = [maxLat, maxLon];
    state.geotiffBounds = L.latLngBounds(southWest, northEast);

    if (state.geotiffOverlay) {
        state.map.removeLayer(state.geotiffOverlay);
    }

    state.geotiffRawDataUrl = "data:image/png;base64," + data.image_base64;
    state.geotiffOverlay = L.imageOverlay(state.geotiffRawDataUrl, state.geotiffBounds, {
        opacity: 1.0,
        interactive: false,
        zIndex: 50
    }).addTo(state.map);

    state.map.fitBounds(state.geotiffBounds, { padding: [30, 30] });
    showToast(`Ortofoto ${data.filename} sobrepuesta con éxito en escala real`);
}

/**
 * Visualización Interactiva de los 234 Puntos de Disparo del Dron (DJI RTK .MRK)
 */
function handleDroneFlightLoaded(data) {
    state.layers.droneFlight.clearLayers();
    state.activeDroneShots = data.shots || [];
    state.selectedDroneFolderPath = data.folder_path;

    const flightCard = document.getElementById('flightInfoCard');
    const flightNameText = document.getElementById('flightNameText');
    const flightShotsCount = document.getElementById('flightShotsCount');
    const flightAvgAlt = document.getElementById('flightAvgAlt');
    const odmFolderPath = document.getElementById('odmFolderPath');
    const odmPhotoSummary = document.getElementById('odmPhotoSummary');
    const odmPhotoCountText = document.getElementById('odmPhotoCountText');

    if (flightCard) flightCard.style.display = 'flex';
    if (flightNameText) flightNameText.textContent = data.folder_name || "VarianteNorteCamino";
    if (flightShotsCount) flightShotsCount.textContent = `${data.shots.length} fotos`;
    if (flightAvgAlt) flightAvgAlt.textContent = `${data.avg_alt_m} m`;

    if (odmFolderPath) odmFolderPath.value = data.folder_path || "";
    if (odmPhotoSummary) odmPhotoSummary.style.display = 'block';
    if (odmPhotoCountText) odmPhotoCountText.textContent = `${data.shots.length} fotos / disparos listos para procesar`;

    if (!data.shots || data.shots.length === 0) return;

    // 1. Dibujar la línea de trayectoria de vuelo
    const trajectoryLatLngs = data.shots.map(s => [s.Latitude, s.Longitude]);
    L.polyline(trajectoryLatLngs, {
        color: '#d05ce3',
        weight: 3,
        dashArray: '5, 5',
        opacity: 0.85
    }).addTo(state.layers.droneFlight);

    // 2. Dibujar marcadores de disparo de cámara individuales
    data.shots.forEach((shot, index) => {
        const isStartOrEnd = index === 0 || index === data.shots.length - 1;
        const color = isStartOrEnd ? '#00e5ff' : '#a371f7';

        const marker = L.circleMarker([shot.Latitude, shot.Longitude], {
            radius: isStartOrEnd ? 6 : 4,
            color: '#ffffff',
            weight: 1.5,
            fillColor: color,
            fillOpacity: 0.95
        });

        const popupContent = `
            <div style="font-family: var(--font-sans); font-size: 11px; min-width: 180px;">
                <strong style="color: var(--accent-purple); font-size: 12px;"><i class="fa-solid fa-camera"></i> Foto #${shot.Index}</strong><br>
                <b>Archivo:</b> ${shot.PhotoFileName}<br>
                <b>Altitud Elipsoidal:</b> ${shot.AltitudeM} m<br>
                <b>Precisión GNSS:</b> <span style="color: #00e676;">${shot.Quality}</span><br>
                <b>GPS:</b> ${shot.Latitude.toFixed(6)}, ${shot.Longitude.toFixed(6)}<br>
                <button onclick="openDronePhotoPreview('${encodeURIComponent(shot.PhotoFullPath)}', '${shot.PhotoFileName}', ${shot.Index}, ${shot.AltitudeM}, ${shot.Latitude}, ${shot.Longitude})" style="margin-top: 6px; width: 100%; background: #238636; color: #fff; border: none; padding: 4px 8px; border-radius: 4px; font-weight: 600; cursor: pointer;">
                    <i class="fa-solid fa-image"></i> Ver Foto Aérea
                </button>
            </div>
        `;

        marker.bindPopup(popupContent);
        marker.bindTooltip(`Foto #${shot.Index} (${shot.PhotoFileName}) - Alt: ${shot.AltitudeM}m`, { sticky: true });
        marker.addTo(state.layers.droneFlight);
    });

    // Ajustar vista a la trayectoria del vuelo
    const southWest = [data.minLat, data.minLon];
    const northEast = [data.maxLat, data.maxLon];
    const bounds = L.latLngBounds(southWest, northEast);
    state.map.fitBounds(bounds, { padding: [40, 40] });

    showToast(`Ruta de vuelo cargada: ${data.shots.length} disparos RTK georreferenciados`);
}

/**
 * Función global para abrir la vista previa de una foto aérea del dron
 */
window.openDronePhotoPreview = function(encodedPath, fileName, index, alt, lat, lon) {
    const fullPath = decodeURIComponent(encodedPath);
    const modal = document.getElementById('dronePhotoModal');
    const title = document.getElementById('photoModalTitle');
    const subtitle = document.getElementById('photoModalSubtitle');
    const coords = document.getElementById('photoModalCoords');
    const loading = document.getElementById('photoModalLoading');
    const img = document.getElementById('photoModalImg');

    if (title) title.textContent = fileName;
    if (subtitle) subtitle.textContent = `Disparo #${index} | RTK Fix | Altitud: ${alt} m`;
    if (coords) coords.textContent = `GPS: ${lat.toFixed(6)}, ${lon.toFixed(6)}`;

    if (loading) loading.style.display = 'block';
    if (img) img.style.display = 'none';
    if (modal) modal.style.display = 'flex';

    sendToCSharp("get_drone_photo_preview", { photo_path: fullPath });
};

function handleDronePhotoPreviewReady(data) {
    const loading = document.getElementById('photoModalLoading');
    const img = document.getElementById('photoModalImg');

    if (loading) loading.style.display = 'none';
    if (img && data.success) {
        img.src = "data:image/jpeg;base64," + data.image_base64;
        img.style.display = 'block';
    } else {
        showToast("No se pudo cargar la vista previa de la foto.");
    }
}

function handlePhotogrammetryProgress(data) {
    const progressBox = document.getElementById('odmProgressBox');
    const progressBar = document.getElementById('odmProgressBar');
    const statusText = document.getElementById('odmStatusText');
    const percentText = document.getElementById('odmPercentText');

    if (progressBox) progressBox.style.display = 'flex';
    if (progressBar) progressBar.style.width = `${data.percent}%`;
    if (percentText) percentText.textContent = `${data.percent}%`;
    if (statusText) statusText.textContent = data.message;

    // Actualizar también la barra en la tarjeta de vuelo lateral
    const flightProgressBox = document.getElementById('flightCardProgressBox');
    const flightProgressBar = document.getElementById('flightCardProgressBar');
    const flightStatusText = document.getElementById('flightCardStatusText');
    const flightPercentText = document.getElementById('flightCardPercentText');

    if (flightProgressBox) flightProgressBox.style.display = 'flex';
    if (flightProgressBar) flightProgressBar.style.width = `${data.percent}%`;
    if (flightPercentText) flightPercentText.textContent = `${data.percent}%`;
    if (flightStatusText) flightStatusText.textContent = data.message;
}

function handlePhotogrammetryCompleted(data) {
    const progressBox = document.getElementById('odmProgressBox');
    const odmModal = document.getElementById('odmModal');
    const flightProgressBox = document.getElementById('flightCardProgressBox');

    if (progressBox) progressBox.style.display = 'none';
    if (odmModal) odmModal.style.display = 'none';
    if (flightProgressBox) flightProgressBox.style.display = 'none';

    // Guardar rutas 3D
    state.dsmPath = data.dsmPath;
    state.dtmPath = data.dtmPath;

    showToast("Mapas generados correctamente. Cargando ortofoto...");
    
    // Cargar automáticamente la ortofoto generada
    if (data.tiffPath) {
        window.chrome.webview.postMessage({
            action: 'load_library_file',
            file_path: data.tiffPath,
            category: 'ortofotos_geotiff'
        });
    }
}

/**
 * Inicializar Mapa Leaflet
 */
function initMap() {
    state.map = L.map('map', {
        center: [-33.4372, -70.6482],
        zoom: 16,
        zoomControl: false,
        maxZoom: 22
    });

    L.control.zoom({ position: 'topleft' }).addTo(state.map);

    state.baseLayers.orthophoto = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
        attribution: 'Satelital &copy; Esri High-Res Imagery',
        maxZoom: 20,
        maxNativeZoom: 19
    });

    state.baseLayers.dark = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
        attribution: '&copy; OpenStreetMap &copy; CARTO',
        maxZoom: 20
    });

    state.baseLayers.osm = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap',
        maxZoom: 19
    });

    state.baseLayers.orthophoto.addTo(state.map);

    state.layers.corridor = L.layerGroup().addTo(state.map);
    state.layers.kmz = L.layerGroup().addTo(state.map);
    state.layers.powerline = L.layerGroup().addTo(state.map);
    state.layers.towers = L.layerGroup().addTo(state.map);
    state.layers.trees = L.layerGroup().addTo(state.map);
    state.layers.droneFlight = L.layerGroup().addTo(state.map);

    // Evento Clic Derecho (Context Menu) para agregar árbol falso negativo
    state.map.on('contextmenu', function(e) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({
                action: 'add_manual_tree',
                lat: e.latlng.lat,
                lon: e.latlng.lng
            });
            showToast("Árbol manual agregado");
        }
    });

    // Centrar automáticamente en la ubicación del equipo (ultraligero)
    locateUserAndCenterMap(false);
}

/**
 * Centrar mapa en la ubicación del equipo (Cero consumo, asíncrono e instantáneo)
 */
function locateUserAndCenterMap(showToastMessage = false) {
    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(
            (pos) => {
                const lat = pos.coords.latitude;
                const lon = pos.coords.longitude;
                state.map.setView([lat, lon], 14, { animate: true });
                if (showToastMessage) showToast("🎯 Mapa centrado en tu ubicación actual");
            },
            (err) => {
                // Fallback rápido por IP
                fetch('https://ipapi.co/json/')
                    .then(r => r.json())
                    .then(data => {
                        if (data && data.latitude && data.longitude) {
                            state.map.setView([data.latitude, data.longitude], 13);
                            if (showToastMessage) showToast(`🎯 Ubicación: ${data.city || 'Chile'}`);
                        } else {
                            state.map.setView([-36.8270, -73.0503], 12);
                        }
                    })
                    .catch(() => {
                        state.map.setView([-36.8270, -73.0503], 12);
                    });
            },
            { timeout: 3500, enableHighAccuracy: false, maximumAge: 60000 }
        );
    }
}

/**
 * Controles de Interfaz
 */
function initUIControls() {
    const corridorSlider = document.getElementById('corridorSlider');
    const corridorSliderVal = document.getElementById('corridorSliderVal');
    const headerCorridorWidth = document.getElementById('headerCorridorWidth');
    const legendCorridorWidth = document.getElementById('legendCorridorWidth');
    const btnRunAnalysis = document.getElementById('btnRunAnalysis');
    const btnCloseModal = document.getElementById('btnCloseModal');
    const treeModal = document.getElementById('treeModal');
    const btnExportPDF = document.getElementById('btnExportPDF');
    const btnExportCSV = document.getElementById('btnExportCSV');
    const btnLoadKMZFile = document.getElementById('btnLoadKMZFile');
    const btnLoadSampleKMZ = document.getElementById('btnLoadSampleKMZ');
    const kmzVisibilityToggle = document.getElementById('kmzVisibilityToggle');
    const btnFocusKMZ = document.getElementById('btnFocusKMZ');
    const btnLoadGeoTiff = document.getElementById('btnLoadGeoTiff');
    const btnFocusGeoTiff = document.getElementById('btnFocusGeoTiff');
    const btnShowTiffDebug = document.getElementById('btnShowTiffDebug');
    const btnAlignTiffWithKmz = document.getElementById('btnAlignTiffWithKmz');
    const orthoOpacitySlider = document.getElementById('orthoOpacitySlider');
    const orthoOpacityVal = document.getElementById('orthoOpacityVal');
    const btnToggleBaseLayer = document.getElementById('btnToggleBaseLayer');
    const btnResetWorkspace = document.getElementById('btnResetWorkspace');
    const btnLocateMe = document.getElementById('btnLocateMe');

    // Botón Mi Ubicación
    if (btnLocateMe) {
        btnLocateMe.addEventListener('click', () => locateUserAndCenterMap(true));
    }

    // Búsqueda de Coordenadas GPS
    const btnSearchGpsCoords = document.getElementById('btnSearchGpsCoords');
    const btnClearGpsMarker = document.getElementById('btnClearGpsMarker');
    const gpsSearchInput = document.getElementById('gpsSearchInput');

    if (btnSearchGpsCoords && gpsSearchInput) {
        btnSearchGpsCoords.addEventListener('click', () => {
            const val = gpsSearchInput.value.trim();
            if (!val) return;
            
            // Intenta parsear "Lat, Lon" o "Lat Lon" o "Lon, Lat" (asumiendo Lat en Chile es negativo ~-30 y Lon ~-70)
            const parts = val.split(/[,\s]+/).map(p => parseFloat(p)).filter(p => !isNaN(p));
            if (parts.length >= 2) {
                let lat = parts[0];
                let lon = parts[1];
                
                // Corrección automática básica si el usuario pone Lon, Lat (Chile: lat ~ -17 a -56, lon ~ -66 a -75)
                if (Math.abs(lon) < 56 && Math.abs(lat) > 60) {
                    const temp = lat;
                    lat = lon;
                    lon = temp;
                }

                if (state.gpsSearchMarker) {
                    state.map.removeLayer(state.gpsSearchMarker);
                }

                state.gpsSearchMarker = L.marker([lat, lon], {
                    icon: L.divIcon({
                        className: 'custom-gps-marker',
                        html: '<i class="fa-solid fa-location-dot fa-2x" style="color: var(--accent-cyan); filter: drop-shadow(0px 4px 4px rgba(0,0,0,0.5));"></i>',
                        iconSize: [30, 30],
                        iconAnchor: [15, 30]
                    })
                }).addTo(state.map);

                state.gpsSearchMarker.bindPopup(`<b>Coordenada Buscada</b><br>${lat.toFixed(6)}, ${lon.toFixed(6)}`).openPopup();
                state.map.flyTo([lat, lon], 19, { animate: true, duration: 1.5 });
                
                if (btnClearGpsMarker) btnClearGpsMarker.style.display = 'block';
            } else {
                showToast("Formato inválido. Usa: Lat, Lon (ej: -37.123, -72.456)");
            }
        });

        // Trigger con Enter
        gpsSearchInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') btnSearchGpsCoords.click();
        });
    }

    if (btnClearGpsMarker) {
        btnClearGpsMarker.addEventListener('click', () => {
            if (state.gpsSearchMarker) {
                state.map.removeLayer(state.gpsSearchMarker);
                state.gpsSearchMarker = null;
            }
            if (gpsSearchInput) gpsSearchInput.value = '';
            btnClearGpsMarker.style.display = 'none';
        });
    }

    // Drone Flight Controls
    const btnLoadFlightShots = document.getElementById('btnLoadFlightShots');
    const flightVisibilityToggle = document.getElementById('flightVisibilityToggle');
    const btnFocusFlight = document.getElementById('btnFocusFlight');

    if (btnLoadFlightShots) {
        btnLoadFlightShots.addEventListener('click', () => {
            sendToCSharp("open_telemetry_file_dialog");
        });
    }

    if (flightVisibilityToggle) {
        flightVisibilityToggle.addEventListener('change', (e) => {
            if (e.target.checked) state.layers.droneFlight.addTo(state.map);
            else state.map.removeLayer(state.layers.droneFlight);
        });
    }

    if (btnFocusFlight) {
        btnFocusFlight.addEventListener('click', () => {
            if (state.activeDroneShots.length > 0) {
                const latlngs = state.activeDroneShots.map(s => [s.Latitude, s.Longitude]);
                const bounds = L.latLngBounds(latlngs);
                state.map.fitBounds(bounds, { padding: [40, 40] });
            }
        });
    }

    // Drone Photo Modal close
    const dronePhotoModal = document.getElementById('dronePhotoModal');
    const btnClosePhotoModal = document.getElementById('btnClosePhotoModal');
    const btnClosePhotoModalBtn = document.getElementById('btnClosePhotoModalBtn');

    if (btnClosePhotoModal) btnClosePhotoModal.addEventListener('click', () => { dronePhotoModal.style.display = 'none'; });
    if (btnClosePhotoModalBtn) btnClosePhotoModalBtn.addEventListener('click', () => { dronePhotoModal.style.display = 'none'; });
    if (dronePhotoModal) dronePhotoModal.addEventListener('click', (e) => { if (e.target === dronePhotoModal) dronePhotoModal.style.display = 'none'; });

    // Botón Limpiar Mapa / Reiniciar
    if (btnResetWorkspace) {
        btnResetWorkspace.addEventListener('click', () => {
            sendToCSharp("clear_workspace");
        });
    }

    // Selectores de Biblioteca de Capas Persistente
    const selectLibraryKMZ = document.getElementById('selectLibraryKMZ');
    const selectLibraryGeoTiff = document.getElementById('selectLibraryGeoTiff');
    const btnOpenKmzLibFolder = document.getElementById('btnOpenKmzLibFolder');
    const btnOpenTiffLibFolder = document.getElementById('btnOpenTiffLibFolder');

    if (selectLibraryKMZ) {
        selectLibraryKMZ.addEventListener('change', (e) => {
            const path = e.target.value;
            if (path) {
                sendToCSharp("load_library_file", { file_path: path, category: "kmz" });
            }
        });
    }

    if (selectLibraryGeoTiff) {
        selectLibraryGeoTiff.addEventListener('change', (e) => {
            const val = e.target.value;
            if (val) {
                try {
                    const parsed = JSON.parse(val);
                    sendToCSharp("load_library_file", { file_path: parsed.path, category: parsed.type });
                } catch {
                    sendToCSharp("load_library_file", { file_path: val, category: "geotiff" });
                }
            }
        });
    }

    if (btnOpenKmzLibFolder) {
        btnOpenKmzLibFolder.addEventListener('click', () => sendToCSharp("open_library_folder"));
    }
    if (btnOpenTiffLibFolder) {
        btnOpenTiffLibFolder.addEventListener('click', () => sendToCSharp("open_library_folder"));
    }

    // WebODM Modal Elements
    const btnOpenOdmModal = document.getElementById('btnOpenOdmModal');
    const odmModal = document.getElementById('odmModal');
    const btnCloseOdmModal = document.getElementById('btnCloseOdmModal');
    const btnCloseOdmModalBtn = document.getElementById('btnCloseOdmModalBtn');
    const btnBrowseDroneFolder = document.getElementById('btnBrowseDroneFolder');
    const btnStartOdmProcessing = document.getElementById('btnStartOdmProcessing');

    if (btnOpenOdmModal) {
        btnOpenOdmModal.addEventListener('click', () => { odmModal.style.display = 'flex'; });
    }
    if (btnCloseOdmModal) btnCloseOdmModal.addEventListener('click', () => { odmModal.style.display = 'none'; });
    if (btnCloseOdmModalBtn) btnCloseOdmModalBtn.addEventListener('click', () => { odmModal.style.display = 'none'; });
    if (odmModal) odmModal.addEventListener('click', (e) => { if (e.target === odmModal) odmModal.style.display = 'none'; });

    if (btnBrowseDroneFolder) {
        btnBrowseDroneFolder.addEventListener('click', () => sendToCSharp("open_drone_photos_dialog"));
    }

    // Botón de generación directa desde la tarjeta de vuelo lateral
    const btnGenerateFlightOrthoDirect = document.getElementById('btnGenerateFlightOrthoDirect');
    if (btnGenerateFlightOrthoDirect) {
        btnGenerateFlightOrthoDirect.addEventListener('click', () => {
            if (!state.selectedDroneFolderPath) {
                showToast("Por favor carga primero la telemetría o fotos del dron");
                return;
            }
            const flightProgressBox = document.getElementById('flightCardProgressBox');
            if (flightProgressBox) flightProgressBox.style.display = 'flex';
            sendToCSharp("start_photogrammetry_processing", {
                folder_path: state.selectedDroneFolderPath,
                preset: "corridor"
            });
        });
    }

    if (btnStartOdmProcessing) {
        btnStartOdmProcessing.addEventListener('click', () => {
            if (!state.selectedDroneFolderPath) {
                showToast("Por favor selecciona una carpeta con fotos o telemetría");
                return;
            }
            const progressBox = document.getElementById('odmProgressBox');
            const progressBar = document.getElementById('odmProgressBar');
            const statusText = document.getElementById('odmStatusText');
            const percentText = document.getElementById('odmPercentText');
            const presetSelect = document.getElementById('odmPresetSelect');

            if (progressBox) progressBox.style.display = 'flex';
            if (progressBar) progressBar.style.width = '5%';
            if (percentText) percentText.textContent = '5%';
            if (statusText) statusText.textContent = 'Enviando imágenes a NodeODM...';

            sendToCSharp("start_nodeodm_processing", {
                folder_path: state.selectedDroneFolderPath,
                preset: presetSelect ? presetSelect.value : "corridor"
            });
        });
    }

    // Modal de Diagnóstico
    const tiffDebugModal = document.getElementById('tiffDebugModal');
    const btnCloseDebugModal = document.getElementById('btnCloseDebugModal');
    const btnCloseDebugModalBtn = document.getElementById('btnCloseDebugModalBtn');

    if (btnShowTiffDebug) {
        btnShowTiffDebug.addEventListener('click', () => {
            document.getElementById('debugModalContent').textContent = state.geotiffDebugInfo;
            tiffDebugModal.style.display = 'flex';
        });
    }

    if (btnCloseDebugModal) btnCloseDebugModal.addEventListener('click', () => { tiffDebugModal.style.display = 'none'; });
    if (btnCloseDebugModalBtn) btnCloseDebugModalBtn.addEventListener('click', () => { tiffDebugModal.style.display = 'none'; });
    if (tiffDebugModal) tiffDebugModal.addEventListener('click', (e) => { if (e.target === tiffDebugModal) tiffDebugModal.style.display = 'none'; });

    // Alinear Ortofoto con KMZ
    if (btnAlignTiffWithKmz) {
        btnAlignTiffWithKmz.addEventListener('click', () => {
            if (!state.activeKMZGeoJSON || !state.geotiffRawDataUrl) {
                showToast("Debes tener cargada una ortofoto y un KMZ para alinear.");
                return;
            }

            const kmzLayer = L.geoJSON(state.activeKMZGeoJSON);
            const kmzBounds = kmzLayer.getBounds();
            if (kmzBounds.isValid()) {
                const padLat = (kmzBounds.getNorth() - kmzBounds.getSouth()) * 0.12;
                const padLon = (kmzBounds.getEast() - kmzBounds.getWest()) * 0.12;
                state.geotiffBounds = L.latLngBounds(
                    [kmzBounds.getSouth() - padLat, kmzBounds.getWest() - padLon],
                    [kmzBounds.getNorth() + padLat, kmzBounds.getEast() + padLon]
                );

                if (state.geotiffOverlay) {
                    state.map.removeLayer(state.geotiffOverlay);
                }

                state.geotiffOverlay = L.imageOverlay(state.geotiffRawDataUrl, state.geotiffBounds, {
                    opacity: 1.0,
                    interactive: false,
                    zIndex: 50
                }).addTo(state.map);

                state.map.fitBounds(state.geotiffBounds, { padding: [20, 20] });
                showToast("Ortofoto alineada con precisión sobre el trazado KMZ");
            }
        });
    }

    // Botón para apagar/encender capa base satelital
    if (btnToggleBaseLayer) {
        btnToggleBaseLayer.addEventListener('click', toggleBaseLayer);
    }

    // Cargar GeoTIFF
    if (btnLoadGeoTiff) {
        btnLoadGeoTiff.addEventListener('click', () => sendToCSharp("open_geotiff_dialog"));
    }

    // Cargar DSM (Modelo Digital de Superficie)
    const btnLoadDsm = document.getElementById('btnLoadDsm');
    if (btnLoadDsm) {
        btnLoadDsm.addEventListener('click', () => sendToCSharp("open_dsm_dialog"));
    }

    // Cargar DTM (Modelo Digital del Terreno)
    const btnLoadDtm = document.getElementById('btnLoadDtm');
    if (btnLoadDtm) {
        btnLoadDtm.addEventListener('click', () => sendToCSharp("open_dtm_dialog"));
    }

    if (btnFocusGeoTiff) {
        btnFocusGeoTiff.addEventListener('click', () => {
            if (state.geotiffBounds) state.map.fitBounds(state.geotiffBounds, { padding: [40, 40] });
        });
    }

    // Opacidad de Ortofoto
    if (orthoOpacitySlider) {
        orthoOpacitySlider.addEventListener('input', (e) => {
            const val = parseFloat(e.target.value);
            orthoOpacityVal.textContent = `${Math.round(val * 100)}%`;
            if (state.geotiffOverlay) state.geotiffOverlay.setOpacity(val);
        });
    }

    // Slider de Servidumbre
    corridorSlider.addEventListener('input', (e) => {
        const val = parseFloat(e.target.value);
        state.corridorWidthM = val;
        corridorSliderVal.textContent = `${val} m`;
        if (headerCorridorWidth) headerCorridorWidth.textContent = `${val} m`;
        if (legendCorridorWidth) legendCorridorWidth.textContent = `${val} m`;
        sendToCSharp("recalculate_corridor", { corridor_width_m: val });
    });

    // Abrir KMZ
    if (btnLoadKMZFile) {
        btnLoadKMZFile.addEventListener('click', () => sendToCSharp("open_kmz_dialog"));
    }

    // Botón Apagar/Encender KMZ
    const btnToggleKMZView = document.getElementById('btnToggleKMZView');
    if (btnToggleKMZView) {
        btnToggleKMZView.addEventListener('click', () => {
            toggleKMZVisibility(!state.isKmzVisible);
        });
    }

    // Visibilidad KMZ Switch
    if (kmzVisibilityToggle) {
        kmzVisibilityToggle.addEventListener('change', (e) => {
            toggleKMZVisibility(e.target.checked);
        });
    }

    // Visibilidad Franja de Servidumbre (20m)
    const corridorVisibilityToggle = document.getElementById('corridorVisibilityToggle');
    const btnToggleCorridorLayer = document.getElementById('btnToggleCorridorLayer');

    if (corridorVisibilityToggle) {
        corridorVisibilityToggle.addEventListener('change', (e) => {
            toggleCorridorVisibility(e.target.checked);
        });
    }

    if (btnToggleCorridorLayer) {
        btnToggleCorridorLayer.addEventListener('click', () => {
            toggleCorridorVisibility(!state.isCorridorVisible);
        });
    }

    // Minimizar / Expandir Simbología
    const mapLegendCard = document.getElementById('mapLegendCard');
    const legendHeaderToggle = document.getElementById('legendHeaderToggle');
    const iconMinimizeLegend = document.getElementById('iconMinimizeLegend');

    if (legendHeaderToggle && mapLegendCard) {
        legendHeaderToggle.addEventListener('click', () => {
            mapLegendCard.classList.toggle('minimized');
            const isMin = mapLegendCard.classList.contains('minimized');
            if (iconMinimizeLegend) {
                iconMinimizeLegend.className = isMin ? 'fa-solid fa-chevron-up' : 'fa-solid fa-chevron-down';
            }
        });
    }

    // Centrar en KMZ
    if (btnFocusKMZ) {
        btnFocusKMZ.addEventListener('click', focusKMZLayer);
    }

    // Sensibilidad Slider
    const sensitivitySlider = document.getElementById('sensitivitySlider');
    const sensitivitySliderVal = document.getElementById('sensitivitySliderVal');
    if (sensitivitySlider && sensitivitySliderVal) {
        const labels = ["", "Nivel 1 (Muy Baja)", "Nivel 2 (Baja)", "Nivel 3 (Media)", "Nivel 4 (Alta)", "Nivel 5 (Máxima)"];
        sensitivitySlider.addEventListener('input', (e) => {
            const val = parseInt(e.target.value);
            sensitivitySliderVal.textContent = labels[val] || `Nivel ${val}`;
        });
    }

    // Ejecutar Análisis Real
    btnRunAnalysis.addEventListener('click', () => {
        const progressBox = document.getElementById('scanProgressBox');
        const progressBar = document.getElementById('scanProgressBar');
        const statusText = document.getElementById('scanStatusText');
        const percentText = document.getElementById('scanPercentText');

        progressBox.style.display = 'flex';
        progressBar.style.width = '0%';

        const sens = sensitivitySlider ? parseInt(sensitivitySlider.value) : 4;

        const phases = [
            { pct: 25, text: "1/4 Leyendo píxeles de ortofoto con decodificador universal..." },
            { pct: 50, text: `2/4 Generando buffer de servidumbre de ${state.corridorWidthM}m...` },
            { pct: 75, text: `3/4 Clasificando especies botánicas (Pino, Eucalipto, Espino) con Nivel ${sens}...` },
            { pct: 100, text: "4/4 Segmentando copas y calculando distancias ortogonales..." }
        ];

        let currentPhase = 0;
        const interval = setInterval(() => {
            if (currentPhase < phases.length) {
                const p = phases[currentPhase];
                progressBar.style.width = `${p.pct}%`;
                percentText.textContent = `${p.pct}%`;
                statusText.textContent = p.text;
                currentPhase++;
            } else {
                clearInterval(interval);
                progressBox.style.display = 'none';
                sendToCSharp("run_real_analysis", { sensitivity: sens });
            }
        }, 280);
    });

    // Exportar PDF en C#
    if (btnExportPDF) {
        btnExportPDF.addEventListener('click', () => sendToCSharp("export_pdf"));
    }

    // Exportar CSV
    if (btnExportCSV) {
        btnExportCSV.addEventListener('click', () => sendToCSharp("export_csv"));
    }

    // Switcher de Capas Base
    document.querySelectorAll('.layer-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const targetLayer = btn.dataset.layer;
            document.querySelectorAll('.layer-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            if (state.baseLayers[state.currentLayer]) {
                state.map.removeLayer(state.baseLayers[state.currentLayer]);
            }

            if (targetLayer === 'none') {
                state.currentLayer = 'none';
                state.isBaseLayerActive = false;
                updateBaseToggleButton(false);
            } else {
                state.baseLayers[targetLayer].addTo(state.map);
                state.currentLayer = targetLayer;
                state.isBaseLayerActive = true;
                updateBaseToggleButton(true);
            }
        });
    });

    // Filtros de Riesgo
    document.querySelectorAll('.filter-pill').forEach(pill => {
        pill.addEventListener('click', () => {
            document.querySelectorAll('.filter-pill').forEach(p => p.classList.remove('active'));
            pill.classList.add('active');
            state.currentFilter = pill.dataset.filter;
            renderTreesOnMap();
            renderTreeTable();
        });
    });

    // Modal
    btnCloseModal.addEventListener('click', () => { treeModal.style.display = 'none'; });
    treeModal.addEventListener('click', (e) => { if (e.target === treeModal) treeModal.style.display = 'none'; });
}

/**
 * Función para encender o apagar el mapa base satelital
 */
function toggleBaseLayer() {
    if (state.isBaseLayerActive) {
        if (state.baseLayers[state.currentLayer]) {
            state.map.removeLayer(state.baseLayers[state.currentLayer]);
        }
        state.isBaseLayerActive = false;
        updateBaseToggleButton(false);
        showToast("Mapa satelital apagado: Mostrando solo ortofoto y vectores");
    } else {
        const layerToActivate = state.currentLayer === 'none' ? 'orthophoto' : state.currentLayer;
        state.baseLayers[layerToActivate].addTo(state.map);
        state.currentLayer = layerToActivate;
        state.isBaseLayerActive = true;
        updateBaseToggleButton(true);
        showToast("Mapa satelital encendido");
    }
}

function updateBaseToggleButton(isActive) {
    const btn = document.getElementById('btnToggleBaseLayer');
    const icon = document.getElementById('iconToggleBase');
    if (!btn || !icon) return;

    if (isActive) {
        btn.classList.remove('base-off');
        icon.className = 'fa-solid fa-eye-slash';
        btn.title = 'Ocultar mapa satelital base';
    } else {
        btn.classList.add('base-off');
        icon.className = 'fa-solid fa-eye';
        btn.title = 'Mostrar mapa satelital base';
    }
}

/**
 * Función para encender o apagar la capa de vectores KMZ (Líneas y Postes)
 */
function toggleKMZVisibility(isVisible) {
    state.isKmzVisible = isVisible;
    const toggle = document.getElementById('kmzVisibilityToggle');
    const btn = document.getElementById('btnToggleKMZView');
    const icon = document.getElementById('iconToggleKMZView');
    const text = document.getElementById('textToggleKMZView');

    if (toggle) toggle.checked = isVisible;

    if (isVisible) {
        if (!state.map.hasLayer(state.layers.kmz)) {
            state.layers.kmz.addTo(state.map);
        }
        if (icon) icon.className = 'fa-solid fa-eye-slash';
        if (text) text.textContent = 'Apagar KMZ';
        if (btn) {
            btn.style.color = 'var(--accent-cyan)';
            btn.style.borderColor = 'rgba(0, 229, 255, 0.4)';
        }
        showToast("Líneas y postes KMZ visibles");
    } else {
        state.map.removeLayer(state.layers.kmz);
        if (icon) icon.className = 'fa-solid fa-eye';
        if (text) text.textContent = 'Encender KMZ';
        if (btn) {
            btn.style.color = 'var(--text-secondary)';
            btn.style.borderColor = 'rgba(255, 255, 255, 0.2)';
        }
        showToast("Líneas y postes KMZ apagados");
    }
}

/**
 * Función para encender o apagar la franja de servidumbre (20m)
 */
function toggleCorridorVisibility(isVisible) {
    state.isCorridorVisible = isVisible;
    const toggle = document.getElementById('corridorVisibilityToggle');
    const btn = document.getElementById('btnToggleCorridorLayer');
    const icon = document.getElementById('iconToggleCorridor');

    if (toggle) toggle.checked = isVisible;

    if (isVisible) {
        state.layers.corridor.addTo(state.map);
        if (btn) {
            btn.style.background = 'rgba(255, 204, 0, 0.15)';
            btn.style.borderColor = 'rgba(255, 204, 0, 0.4)';
            btn.style.color = 'var(--risk-medium)';
            btn.title = 'Ocultar Servidumbre (20m)';
        }
        if (icon) icon.className = 'fa-solid fa-draw-polygon';
        showToast("Franja de servidumbre visible (20m)");
    } else {
        state.map.removeLayer(state.layers.corridor);
        if (btn) {
            btn.style.background = 'rgba(255, 255, 255, 0.05)';
            btn.style.borderColor = 'rgba(255, 255, 255, 0.1)';
            btn.style.color = 'var(--text-secondary)';
            btn.title = 'Mostrar Servidumbre (20m)';
        }
        if (icon) icon.className = 'fa-solid fa-vector-square';
        showToast("Franja de servidumbre oculta");
    }
}

/**
 * Renderizar Polígono de Servidumbre desde C# (Soporta múltiples tramos sin saltos diagonales)
 */
function renderCorridorPolygon(data) {
    state.layers.corridor.clearLayers();
    if (!data) return;

    // Detectar si es un arreglo de múltiples polígonos (MultiPolygon)
    if (Array.isArray(data) && data.length > 0 && Array.isArray(data[0]) && Array.isArray(data[0][0])) {
        data.forEach(polyCoords => {
            if (polyCoords.length >= 3) {
                const latlngs = polyCoords.map(c => [c[1], c[0]]);
                const polygon = L.polygon(latlngs, {
                    color: '#ffcc00',
                    weight: 1.5,
                    dashArray: '4, 4',
                    fillColor: '#ffcc00',
                    fillOpacity: 0.18
                });
                polygon.bindTooltip(`Servidumbre Legal: ${state.corridorWidthM}m`, { sticky: true });
                polygon.addTo(state.layers.corridor);
            }
        });
    } else if (Array.isArray(data) && data.length >= 3) {
        // Polígono único
        const latlngs = data.map(c => [c[1], c[0]]);
        const polygon = L.polygon(latlngs, {
            color: '#ffcc00',
            weight: 2.0,
            dashArray: '6, 6',
            fillColor: '#ffcc00',
            fillOpacity: 0.22
        });
        polygon.bindTooltip(`Servidumbre Legal: ${state.corridorWidthM}m`, { sticky: true });
        polygon.addTo(state.layers.corridor);
    }

    if (state.isCorridorVisible) {
        if (!state.map.hasLayer(state.layers.corridor)) {
            state.layers.corridor.addTo(state.map);
        }
    } else {
        state.map.removeLayer(state.layers.corridor);
    }
}

function renderPowerlineAndTowers() {
    state.layers.powerline.clearLayers();
    state.layers.towers.clearLayers();
    if (!state.currentMission) return;

    const latlngs = state.currentMission.Towers.map(t => [t.Latitude, t.Longitude]);
    
    L.polyline(latlngs, { color: '#00e5ff', weight: 6, opacity: 0.35 }).addTo(state.layers.powerline);
    L.polyline(latlngs, { color: '#ffffff', weight: 2.5, dashArray: '8, 4', opacity: 0.95 }).addTo(state.layers.powerline);

    state.currentMission.Towers.forEach(t => {
        const towerIcon = L.divIcon({
            className: 'tower-div-icon',
            html: `<div style="background:#ff9900; color:#000; font-size:10px; font-weight:bold; border-radius:4px; padding:2px 5px; border:1px solid #fff; box-shadow:0 0 8px rgba(255,153,0,0.8); display:flex; align-items:center; gap:3px;"><i class="fa-solid fa-tower-broadcast"></i> ${t.Id}</div>`,
            iconSize: [52, 20],
            iconAnchor: [26, 10]
        });

        L.marker([t.Latitude, t.Longitude], { icon: towerIcon })
            .bindPopup(`<strong>${t.Name}</strong><br>Altura: ${t.HeightM}m`)
            .addTo(state.layers.towers);
    });
}

/**
 * Renderizar Capa KMZ sobre Ortofoto
 */
function renderKMZOnMap(geojson, filename) {
    state.layers.kmz.clearLayers();
    state.activeKMZGeoJSON = geojson;
    state.activeKMZFileName = filename || "archivo.kmz";

    const fileNameElem = document.getElementById('kmzFileName');
    const countElem = document.getElementById('kmzFeatureCount');
    if (fileNameElem) fileNameElem.textContent = state.activeKMZFileName;
    if (countElem) countElem.textContent = geojson.features.length;

    geojson.features.forEach(feature => {
        const geom = feature.geometry;
        const props = feature.properties || {};

        if (geom.type === "LineString") {
            const latlngs = geom.coordinates.map(c => [c[1], c[0]]);
            L.polyline(latlngs, { color: '#00e5ff', weight: 8, opacity: 0.35, lineCap: 'round' }).addTo(state.layers.kmz);
            const line = L.polyline(latlngs, { color: '#00ffff', weight: 3.5, opacity: 1.0, dashArray: '10, 4' }).addTo(state.layers.kmz);
            line.bindPopup(`<strong><i class="fa-solid fa-bolt"></i> ${props.name || 'Línea de Transmisión KMZ'}</strong><br>${props.description || ''}`);
        } else if (geom.type === "Point") {
            const latlng = [geom.coordinates[1], geom.coordinates[0]];
            const icon = L.divIcon({
                html: `<div style="background:#ff9900; color:#000; font-size:10px; font-weight:bold; border-radius:4px; padding:2px 6px; border:1px solid #fff; box-shadow:0 0 10px rgba(255,153,0,0.9); display:flex; align-items:center; gap:4px;"><i class="fa-solid fa-tower-broadcast"></i> ${props.name ? props.name.substring(0, 10) : 'TORRE'}</div>`,
                iconSize: [60, 22],
                iconAnchor: [30, 11]
            });
            L.marker(latlng, { icon }).bindPopup(`<strong>${props.name || 'Torre'}</strong><br>${props.description || ''}`).addTo(state.layers.kmz);
        }
    });

    focusKMZLayer();
}

function focusKMZLayer() {
    if (!state.activeKMZGeoJSON || !state.activeKMZGeoJSON.features.length) return;
    const layer = L.geoJSON(state.activeKMZGeoJSON);
    const bounds = layer.getBounds();
    if (bounds.isValid()) state.map.fitBounds(bounds, { padding: [50, 50] });
}

/**
 * Renderizar Árboles con Especies
 */
function renderTreesOnMap() {
    state.layers.trees.clearLayers();
    const filtered = getFilteredTrees();

    filtered.forEach(t => {
        let speciesIcon = t.Species.includes("Pino") ? "🌲" : (t.Species.includes("Eucalipto") ? "🌿" : "🌳");

        const crownCircle = L.circle([t.Latitude, t.Longitude], {
            radius: t.CrownDiameterM / 2.0,
            color: t.RiskColor,
            weight: 2,
            fillColor: t.RiskColor,
            fillOpacity: t.IsInsideCorridor ? 0.5 : 0.25,
            className: 'interactive-tree-circle'
        });

        const centerMarker = L.circleMarker([t.Latitude, t.Longitude], {
            radius: t.RiskLevel === 'CRITICO' ? 6.5 : 5,
            color: '#ffffff',
            weight: 2,
            fillColor: t.RiskColor,
            fillOpacity: 1.0,
            className: 'interactive-tree-marker'
        });

        crownCircle.bindTooltip(`<strong>${t.Id}</strong> | ${speciesIcon} ${t.Species}<br>Dist: <b>${t.DistanceToCableM}m</b> | Altura: <b>${t.HeightM}m</b> | Riesgo: <b>${t.RiskLevel}</b>`, { sticky: true });
        centerMarker.bindTooltip(`<strong>${t.Id}</strong> | ${speciesIcon} ${t.Species}<br>Dist: <b>${t.DistanceToCableM}m</b> | Altura: <b>${t.HeightM}m</b> | Riesgo: <b>${t.RiskLevel}</b>`, { sticky: true });

        // Eventos directos de clic garantizados en Leaflet
        crownCircle.on('click', (e) => {
            if (e && e.originalEvent) L.DomEvent.stopPropagation(e);
            selectTree(t, 'map');
        });

        centerMarker.on('click', (e) => {
            if (e && e.originalEvent) L.DomEvent.stopPropagation(e);
            selectTree(t, 'map');
        });

        crownCircle.addTo(state.layers.trees);
        centerMarker.addTo(state.layers.trees);
    });

    if (typeof updateHeatmap === 'function') {
        updateHeatmap();
    }
}

/**
 * Selección e iluminación bidireccional de árboles (Mapa <-> Tabla de Informe)
 */
function selectTree(tree, triggerSource = 'map') {
    if (!tree) return;
    state.selectedTreeId = tree.Id;

    // 1. Resaltar visualmente la fila en la tabla de inventario
    const allRows = document.querySelectorAll('#treeTableBody tr');
    allRows.forEach(row => row.classList.remove('selected-tree-row'));

    const targetRow = document.getElementById(`row-tree-${tree.Id}`);
    if (targetRow) {
        targetRow.classList.add('selected-tree-row');
        
        // Scroll vertical preciso del contenedor para centrar la fila
        const container = document.querySelector('.table-container');
        if (container) {
            const containerRect = container.getBoundingClientRect();
            const rowRect = targetRow.getBoundingClientRect();
            const scrollOffset = (rowRect.top - containerRect.top) + container.scrollTop - (container.clientHeight / 2) + (rowRect.height / 2);
            container.scrollTo({ top: scrollOffset, behavior: 'smooth' });
        }
    }

    // 2. Anillo de selección en el mapa
    if (state.layers.powerline) {
        if (state.selectedTreeHighlight) {
            state.map.removeLayer(state.selectedTreeHighlight);
        }
        state.selectedTreeHighlight = L.circle([tree.Latitude, tree.Longitude], {
            radius: (tree.CrownDiameterM / 2.0) + 1.2,
            color: '#00e5ff',
            weight: 3.5,
            dashArray: '4, 4',
            fillColor: '#00e5ff',
            fillOpacity: 0.25
        }).addTo(state.map);
    }

    // 3. Si se seleccionó desde la tabla, centrar el mapa suavemente
    if (triggerSource === 'table') {
        state.map.flyTo([tree.Latitude, tree.Longitude], Math.max(state.map.getZoom(), 18), {
            duration: 0.8
        });
    }

    // 4. Abrir ficha técnica con detalles y recomendación en el modal
    openTreeModal(tree);
    showToast(`Árbol seleccionado: ${tree.Id} | ${tree.Species} | Dist: ${tree.DistanceToCableM}m | Riesgo: ${tree.RiskLevel}`);
}

function getFilteredTrees() {
    if (state.currentFilter === 'all') return state.analyzedTrees;
    const filterArray = state.currentFilter.split(',');
    return state.analyzedTrees.filter(t => filterArray.includes(t.RiskLevel));
}

function renderTreeTable() {
    const tbody = document.getElementById('treeTableBody');
    tbody.innerHTML = '';
    const filtered = getFilteredTrees();
    document.getElementById('tableRecordCount').textContent = `${filtered.length} registros`;

    filtered.forEach(t => {
        const tr = document.createElement('tr');
        tr.id = `row-tree-${t.Id}`;
        if (state.selectedTreeId === t.Id) {
            tr.classList.add('selected-tree-row');
        }

        let tagClass = t.RiskLevel === 'CRITICO' ? 'tag-crit' : (t.RiskLevel === 'ALTO' ? 'tag-high' : 'tag-med');
        let speciesIcon = t.Species.includes("Pino") ? "🌲" : (t.Species.includes("Eucalipto") ? "🌿" : "🌳");

        tr.innerHTML = `
            <td><strong>${t.Id}</strong></td>
            <td><span style="font-size:10.5px; color: var(--accent-cyan); font-weight: 500;">${speciesIcon} ${t.Species}</span></td>
            <td>${t.DistanceToCableM} m</td>
            <td><span class="status-tag ${tagClass}">${t.RiskLevel}</span></td>
            <td>${t.HeightM}m</td>
            <td><span style="font-size:10px;">${t.RecommendedAction || 'N/A'}</span></td>
            <td><button class="btn-table-focus" title="Centrar en mapa y abrir ficha"><i class="fa-solid fa-crosshairs"></i></button></td>
        `;
        tr.addEventListener('click', () => selectTree(t, 'table'));
        tr.querySelector('.btn-table-focus').addEventListener('click', (e) => {
            e.stopPropagation();
            selectTree(t, 'table');
        });
        tbody.appendChild(tr);
    });
}

let riskChart = null;

function updateMetricsAndUI() {
    const crit = state.analyzedTrees.filter(t => t.RiskLevel === 'CRITICO').length;
    const alto = state.analyzedTrees.filter(t => t.RiskLevel === 'ALTO').length;
    const medio = state.analyzedTrees.filter(t => t.RiskLevel === 'MEDIO').length;
    const bajo = state.analyzedTrees.filter(t => t.RiskLevel === 'BAJO').length;

    document.getElementById('countAll').textContent = state.analyzedTrees.length;
    document.getElementById('countCrit').textContent = crit;
    document.getElementById('countMed').textContent = alto + medio;
    document.getElementById('countLow').textContent = bajo;
    
    updatePieChart(crit, alto, medio, bajo);
}

function updatePieChart(crit, alto, medio, bajo) {
    const ctx = document.getElementById('riskPieChart');
    if (!ctx) return;
    
    const data = {
        labels: ['Crtico', 'Alto', 'Medio', 'Bajo'],
        datasets: [{
            data: [crit, alto, medio, bajo],
            backgroundColor: ['#ff3366', '#ff9900', '#ffcc00', '#00e676'],
            borderWidth: 0,
            hoverOffset: 4
        }]
    };
    
    if (riskChart) {
        riskChart.data = data;
        riskChart.update();
    } else {
        riskChart = new Chart(ctx, {
            type: 'doughnut',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'right',
                        labels: { color: '#e2e8f0', font: { size: 11, family: 'Segoe UI' } }
                    }
                }
            }
        });
    }
}

function updateTreeModalData(tree) {
    if (!tree) return;
    const elem = (id) => document.getElementById(id);
    if (elem('modalTreeId')) elem('modalTreeId').textContent = tree.Id;
    if (elem('modalTreeSpecies')) elem('modalTreeSpecies').textContent = tree.Species;
    if (elem('modalTreeDist')) elem('modalTreeDist').textContent = `${tree.DistanceToCableM} m`;
    if (elem('modalTreeRisk')) {
        elem('modalTreeRisk').textContent = tree.RiskLevel;
        elem('modalTreeRisk').style.color = tree.RiskColor;
    }
    if (elem('modalTreeHeight')) elem('modalTreeHeight').textContent = `${tree.HeightM} m`;
    if (elem('modalTreeHeightInput')) elem('modalTreeHeightInput').value = tree.HeightM;
    if (elem('modalTreeCrown')) elem('modalTreeCrown').textContent = `${tree.CrownDiameterM} m`;
    if (elem('modalTreeCoords')) elem('modalTreeCoords').textContent = `${tree.Latitude.toFixed(6)}, ${tree.Longitude.toFixed(6)}`;
    if (elem('modalTreeConf')) elem('modalTreeConf').textContent = `${tree.ConfidencePct}% (Identificación Botánica)`;
    if (elem('modalTreeAction')) elem('modalTreeAction').textContent = tree.RecommendedAction;

    const btnDelete = elem('btnFocusMapFromModal'); // Ese es el ID que usamos para eliminar
    if (btnDelete) {
        btnDelete.onclick = () => {
            window.chrome.webview.postMessage({
                action: 'delete_tree',
                tree_id: tree.Id
            });
            const modal = elem('treeModal');
            if (modal) modal.style.display = 'none';
            showToast("Árbol eliminado (Falso Positivo)");
        };
    }

    const btnFocusDefault = elem('btnFocusMapFromModalDefault');
    if (btnFocusDefault) {
        btnFocusDefault.onclick = () => {
            const modal = elem('treeModal');
            if (modal) modal.style.display = 'none';
            state.map.flyTo([tree.Latitude, tree.Longitude], 19, { duration: 1.0 });
        };
    }

    const btnUpdateHeight = elem('btnUpdateHeight');
    if (btnUpdateHeight) {
        btnUpdateHeight.onclick = () => {
            const newHeight = parseFloat(elem('modalTreeHeightInput').value);
            window.chrome.webview.postMessage({
                action: 'update_tree_height',
                tree_id: tree.Id,
                height_m: newHeight
            });
            const modal = elem('treeModal');
            if (modal) modal.style.display = 'none';
        };
    }
}

function openTreeModal(tree) {
    updateTreeModalData(tree);
    const modal = document.getElementById('treeModal');
    if (modal) modal.style.display = 'flex';
}

function openTreeModalById(id) {
    const tree = state.analyzedTrees.find(t => t.Id === id);
    if (tree) openTreeModal(tree);
}
window.openTreeModalById = openTreeModalById;

function showToast(msg) {
    const toast = document.getElementById('toastBox');
    document.getElementById('toastMessage').textContent = msg;
    toast.classList.add('show');
    setTimeout(() => { toast.classList.remove('show'); }, 3500);
}

/**
 * Actualizar las listas desplegables de la Biblioteca de Capas Persistente
 */
function updateLibraryUI(catalog) {
    if (!catalog) return;

    // 1. Selector de Capas KMZ / KML
    const selectKmz = document.getElementById('selectLibraryKMZ');
    if (selectKmz) {
        selectKmz.innerHTML = '<option value="">📁 Seleccionar capa guardada en biblioteca...</option>';
        if (catalog.KmzLayers && catalog.KmzLayers.length > 0) {
            catalog.KmzLayers.forEach(item => {
                const opt = document.createElement('option');
                opt.value = item.FullPath;
                opt.textContent = `⚡ ${item.Name} (${item.SizeFormatted})`;
                selectKmz.appendChild(opt);
            });
        } else {
            const opt = document.createElement('option');
            opt.disabled = true;
            opt.textContent = "-- Sin capas guardadas aún --";
            selectKmz.appendChild(opt);
        }
    }

    // 2. Selector de Ortofotos y Telemetría
    const selectGeo = document.getElementById('selectLibraryGeoTiff');
    if (selectGeo) {
        selectGeo.innerHTML = '<option value="">🗺️ Seleccionar ortofoto / telemetría guardada...</option>';
        
        if (catalog.GeoTiffs && catalog.GeoTiffs.length > 0) {
            const grp = document.createElement('optgroup');
            grp.label = "Ortofotos GeoTIFF";
            catalog.GeoTiffs.forEach(item => {
                const opt = document.createElement('option');
                opt.value = JSON.stringify({ path: item.FullPath, type: "geotiff" });
                opt.textContent = `🗺️ ${item.Name} (${item.SizeFormatted})`;
                grp.appendChild(opt);
            });
            selectGeo.appendChild(grp);
        }

        if (catalog.TelemetryFiles && catalog.TelemetryFiles.length > 0) {
            const grp = document.createElement('optgroup');
            grp.label = "Telemetría de Vuelo (.MRK / .CSV)";
            catalog.TelemetryFiles.forEach(item => {
                const opt = document.createElement('option');
                opt.value = JSON.stringify({ path: item.FullPath, type: "telemetry" });
                opt.textContent = `✈️ ${item.Name} (${item.SizeFormatted})`;
                grp.appendChild(opt);
            });
            selectGeo.appendChild(grp);
        }

        if ((!catalog.GeoTiffs || !catalog.GeoTiffs.length) && (!catalog.TelemetryFiles || !catalog.TelemetryFiles.length)) {
            const opt = document.createElement('option');
            opt.disabled = true;
            opt.textContent = "-- Sin ortofotos guardadas aún --";
            selectGeo.appendChild(opt);
        }
    }
}

// --- HERRAMIENTA DE MEDICI�N MANUAL ---
let measureState = {
    active: false,
    points: [],
    polyline: null,
    markers: [],
    tooltip: null
};

document.addEventListener('DOMContentLoaded', () => {
    setTimeout(() => {
        const btnMeasure = document.getElementById('btnMeasureTool');
        if (btnMeasure) {
            btnMeasure.addEventListener('click', toggleMeasureTool);
        }
    }, 1000);
});

function toggleMeasureTool() {
    measureState.active = !measureState.active;
    const btnMeasure = document.getElementById('btnMeasureTool');
    if (measureState.active) {
        btnMeasure.style.background = 'rgba(255, 51, 102, 0.2)';
        btnMeasure.style.color = '#ff3366';
        btnMeasure.style.borderColor = '#ff3366';
        btnMeasure.title = 'Cancelar Medición';
        document.getElementById('map').style.cursor = 'crosshair';
        showToast("Herramienta de regla activa. Haz clic en el mapa para marcar el punto inicial.");
        state.map.on('click', onMeasureClick);
        state.map.on('mousemove', onMeasureMove);
    } else {
        btnMeasure.style.background = 'rgba(0, 229, 255, 0.2)';
        btnMeasure.style.color = '#00e5ff';
        btnMeasure.style.borderColor = '#00e5ff';
        btnMeasure.title = 'Herramienta Medir Distancia';
        document.getElementById('map').style.cursor = '';
        state.map.off('click', onMeasureClick);
        state.map.off('mousemove', onMeasureMove);
        clearMeasure();
    }
}
function clearMeasure() {
    if (measureState.polyline) state.map.removeLayer(measureState.polyline);
    measureState.markers.forEach(m => state.map.removeLayer(m));
    if (measureState.tooltip) state.map.removeLayer(measureState.tooltip);
    measureState.points = [];
    measureState.polyline = null;
    measureState.markers = [];
    measureState.tooltip = null;
}

function onMeasureClick(e) {
    if (measureState.points.length >= 2) {
        clearMeasure();
    }
    
    measureState.points.push(e.latlng);
    const marker = L.circleMarker(e.latlng, { radius: 5, color: '#ffcc00', fillColor: '#ffcc00', fillOpacity: 1 }).addTo(state.map);
    measureState.markers.push(marker);

    if (measureState.points.length === 1) {
        showToast("Punto inicial marcado. Haz clic en el segundo punto (la l�nea el�ctrica).");
        measureState.polyline = L.polyline([e.latlng, e.latlng], { color: '#ffcc00', weight: 3, dashArray: '5,5' }).addTo(state.map);
        measureState.tooltip = L.tooltip({ permanent: true, direction: 'right', className: 'measure-tooltip' }).setLatLng(e.latlng).addTo(state.map);
    } else if (measureState.points.length === 2) {
        const dist = state.map.distance(measureState.points[0], measureState.points[1]);
        measureState.polyline.setLatLngs(measureState.points);
        measureState.tooltip.setLatLng(e.latlng).setContent('<b>' + dist.toFixed(1) + ' m</b>');
        showToast("Medici�n finalizada: " + dist.toFixed(1) + " metros.");
        // We keep the tool active so they can measure again if they want by clicking again
    }
}

function onMeasureMove(e) {
    if (measureState.points.length === 1) {
        measureState.polyline.setLatLngs([measureState.points[0], e.latlng]);
        const dist = state.map.distance(measureState.points[0], e.latlng);
        measureState.tooltip.setLatLng(e.latlng).setContent('<b>' + dist.toFixed(1) + ' m</b>');
    }
}

// --- MAPA DE CALOR ---
document.addEventListener('DOMContentLoaded', () => {
    setTimeout(() => {
        const btnHeatmap = document.getElementById('btnToggleHeatmap');
        if (btnHeatmap) {
            btnHeatmap.addEventListener('click', toggleHeatmap);
        }
    }, 1000);
});

function toggleHeatmap() {
    state.isHeatmapActive = !state.isHeatmapActive;
    const btnHeatmap = document.getElementById('btnToggleHeatmap');
    if (state.isHeatmapActive) {
        btnHeatmap.style.background = 'rgba(255, 100, 0, 0.4)';
        btnHeatmap.style.color = '#fff';
        showToast("Mapa de Calor Activado (Árboles individuales ocultos)");
        state.map.removeLayer(state.layers.trees);
        updateHeatmap();
        
        // Agregar listener para zoom dinámico
        state.map.on('zoomend', updateHeatmap);
    } else {
        btnHeatmap.style.background = 'rgba(255, 100, 0, 0.2)';
        btnHeatmap.style.color = '#ff6600';
        showToast("Mapa de Calor Desactivado");
        state.layers.trees.addTo(state.map);
        if (state.heatmapLayer) {
            state.map.removeLayer(state.heatmapLayer);
            state.heatmapLayer = null;
        }
        
        // Remover listener
        state.map.off('zoomend', updateHeatmap);
    }
}

function updateHeatmap() {
    if (!state.isHeatmapActive) return;
    if (state.heatmapLayer) {
        state.map.removeLayer(state.heatmapLayer);
    }
    
    if (!state.analyzedTrees || state.analyzedTrees.length === 0) return;
    
    // Preparar puntos para el heatmap
    const heatPoints = [];
    state.analyzedTrees.forEach(t => {
        let intensity = 0.1;
        if (t.RiskLevel === 'CRITICO') intensity = 1.0;
        else if (t.RiskLevel === 'ALTO') intensity = 0.7;
        else if (t.RiskLevel === 'MEDIO') intensity = 0.4;
        
        heatPoints.push([t.Latitude, t.Longitude, intensity]);
    });
    
    // Calcular radio dinámico basado en zoom
    const zoom = state.map.getZoom();
    let dynamicRadius = 40;
    let dynamicBlur = 30;
    
    if (zoom <= 14) { dynamicRadius = 8; dynamicBlur = 6; }
    else if (zoom === 15) { dynamicRadius = 14; dynamicBlur = 10; }
    else if (zoom === 16) { dynamicRadius = 22; dynamicBlur = 18; }
    else if (zoom === 17) { dynamicRadius = 30; dynamicBlur = 25; }
    else if (zoom >= 18) { dynamicRadius = 45; dynamicBlur = 35; }

    state.heatmapLayer = L.heatLayer(heatPoints, {
        radius: dynamicRadius,
        blur: dynamicBlur,
        maxZoom: 18,
        max: 2.0, // Requiere superposición de varios árboles
        gradient: {
            0.1: '#00ff00',
            0.4: '#ffff00',
            0.7: '#ff9900',
            1.0: '#ff0000'
        }
    }).addTo(state.map);
}

