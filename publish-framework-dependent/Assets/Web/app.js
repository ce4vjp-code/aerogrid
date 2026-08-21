/**
 * PowerScan 3D - Edición C# .NET 10
 * Puente bidireccional WebView2 <-> NetTopologySuite y QuestPDF
 */

const state = {
    map: null,
    baseLayers: {},
    currentLayer: 'orthophoto',
    corridorWidthM: 20.0,
    currentMission: null,
    analyzedTrees: [],
    currentFilter: 'all',
    activeKMZGeoJSON: null,
    activeKMZFileName: "LAT_220kV_Muestra.kmz",
    layers: {
        corridor: null,
        kmz: null,
        powerline: null,
        towers: null,
        trees: null
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

        // Solicitar inicialización de datos a C#
        sendToCSharp("init");
    } else {
        console.warn("WebView2 no detectado. Corriendo en modo independiente.");
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
            state.analyzedTrees = payload.trees;
            state.corridorWidthM = payload.corridor_width_m;
            renderCorridorPolygon(payload.corridor_polygon);
            renderPowerlineAndTowers();
            renderTreesOnMap();
            renderTreeTable();
            updateMetricsAndUI();
            // Cargar KMZ de muestra
            sendToCSharp("load_sample_kmz");
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
            showToast(`KMZ cargado con éxito en C#: ${payload.filename}`);
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
 * Inicializar Mapa Leaflet con Ortofoto Dron HD
 */
function initMap() {
    state.map = L.map('map', {
        center: [-33.4372, -70.6482],
        zoom: 16,
        zoomControl: false,
        maxZoom: 20
    });

    L.control.zoom({ position: 'topleft' }).addTo(state.map);

    // Ortofoto Dron HD (Principal)
    state.baseLayers.orthophoto = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
        attribution: 'Ortofoto Dron HD &copy; Esri High-Res Imagery',
        maxZoom: 20,
        maxNativeZoom: 19
    });

    // Carto Dark
    state.baseLayers.dark = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
        attribution: '&copy; OpenStreetMap &copy; CARTO',
        maxZoom: 20
    });

    // OSM
    state.baseLayers.osm = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap',
        maxZoom: 19
    });

    state.baseLayers.orthophoto.addTo(state.map);

    // Capas Vectoriales
    state.layers.corridor = L.layerGroup().addTo(state.map);
    state.layers.kmz = L.layerGroup().addTo(state.map);
    state.layers.powerline = L.layerGroup().addTo(state.map);
    state.layers.towers = L.layerGroup().addTo(state.map);
    state.layers.trees = L.layerGroup().addTo(state.map);
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
    const orthoOpacitySlider = document.getElementById('orthoOpacitySlider');
    const orthoOpacityVal = document.getElementById('orthoOpacityVal');

    // Opacidad de Ortofoto
    if (orthoOpacitySlider) {
        orthoOpacitySlider.addEventListener('input', (e) => {
            const val = parseFloat(e.target.value);
            orthoOpacityVal.textContent = `${Math.round(val * 100)}%`;
            if (state.baseLayers.orthophoto) state.baseLayers.orthophoto.setOpacity(val);
        });
    }

    // Slider de Servidumbre (Dispara recálculo en NetTopologySuite C#)
    corridorSlider.addEventListener('input', (e) => {
        const val = parseFloat(e.target.value);
        state.corridorWidthM = val;
        corridorSliderVal.textContent = `${val} m`;
        headerCorridorWidth.textContent = `${val} m`;
        legendCorridorWidth.textContent = `${val} m`;
        sendToCSharp("recalculate_corridor", { corridor_width_m: val });
    });

    // Abrir Diálogo Nativo de Windows para KMZ
    if (btnLoadKMZFile) {
        btnLoadKMZFile.addEventListener('click', () => sendToCSharp("open_kmz_dialog"));
    }

    // Cargar KMZ de muestra
    if (btnLoadSampleKMZ) {
        btnLoadSampleKMZ.addEventListener('click', () => sendToCSharp("load_sample_kmz"));
    }

    // Visibilidad KMZ
    if (kmzVisibilityToggle) {
        kmzVisibilityToggle.addEventListener('change', (e) => {
            if (e.target.checked) state.layers.kmz.addTo(state.map);
            else state.map.removeLayer(state.layers.kmz);
        });
    }

    // Centrar en KMZ
    if (btnFocusKMZ) {
        btnFocusKMZ.addEventListener('click', focusKMZLayer);
    }

    // Ejecutar Análisis
    btnRunAnalysis.addEventListener('click', runSimulatedScan);

    // Exportar PDF en C# (QuestPDF)
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

            if (state.baseLayers[state.currentLayer]) state.map.removeLayer(state.baseLayers[state.currentLayer]);
            state.baseLayers[targetLayer].addTo(state.map);
            state.currentLayer = targetLayer;
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
 * Renderizar Polígono de Servidumbre desde C#
 */
function renderCorridorPolygon(coords) {
    state.layers.corridor.clearLayers();
    if (!coords || !coords.length) return;

    const latlngs = coords.map(c => [c[1], c[0]]);
    const polygon = L.polygon(latlngs, {
        color: '#ffcc00',
        weight: 2,
        dashArray: '5, 5',
        fillColor: '#ffcc00',
        fillOpacity: 0.18
    });

    polygon.bindTooltip(`Servidumbre: ${state.corridorWidthM}m sobre Ortomosaico`, { sticky: true });
    polygon.addTo(state.layers.corridor);
}

/**
 * Renderizar Línea de Conductor y Torres
 */
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

    document.getElementById('kmzFileName').textContent = state.activeKMZFileName;
    document.getElementById('kmzFeatureCount').textContent = geojson.features.length;

    geojson.features.forEach(feature => {
        const geom = feature.geometry;
        const props = feature.properties || {};

        if (geom.type === "LineString") {
            const latlngs = geom.coordinates.map(c => [c[1], c[0]]);
            L.polyline(latlngs, { color: '#00e5ff', weight: 8, opacity: 0.35, lineCap: 'round' }).addTo(state.layers.kmz);
            const line = L.polyline(latlngs, { color: '#00ffff', weight: 3.5, opacity: 1.0, dashArray: '10, 4' }).addTo(state.layers.kmz);
            line.bindPopup(`<strong><i class="fa-solid fa-bolt"></i> ${props.name || 'Línea KMZ'}</strong><br>${props.description || ''}`);
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
 * Renderizar Árboles
 */
function renderTreesOnMap() {
    state.layers.trees.clearLayers();
    const filtered = getFilteredTrees();

    filtered.forEach(t => {
        const crownCircle = L.circle([t.Latitude, t.Longitude], {
            radius: t.CrownDiameterM / 2.0,
            color: t.RiskColor,
            weight: 1.5,
            fillColor: t.RiskColor,
            fillOpacity: t.IsInsideCorridor ? 0.45 : 0.2
        });

        const centerMarker = L.circleMarker([t.Latitude, t.Longitude], {
            radius: t.RiskLevel === 'CRITICO' ? 6 : 4.5,
            color: '#ffffff',
            weight: 1.5,
            fillColor: t.RiskColor,
            fillOpacity: 1.0
        });

        const group = L.layerGroup([crownCircle, centerMarker]);
        group.on('click', () => openTreeModal(t));
        group.bindTooltip(`<strong>${t.Id}</strong> | Dist: ${t.DistanceToCableM}m<br>Riesgo: <b>${t.RiskLevel}</b>`, { sticky: true });
        group.addTo(state.layers.trees);
    });
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
        let tagClass = t.RiskLevel === 'CRITICO' ? 'tag-crit' : (t.RiskLevel === 'ALTO' ? 'tag-high' : 'tag-med');
        tr.innerHTML = `
            <td><strong>${t.Id}</strong></td>
            <td>${t.DistanceToCableM} m</td>
            <td><span class="status-tag ${tagClass}">${t.RiskLevel}</span></td>
            <td>${t.HeightM} m</td>
            <td><button class="btn-table-focus"><i class="fa-solid fa-crosshairs"></i></button></td>
        `;
        tr.addEventListener('click', () => openTreeModal(t));
        tr.querySelector('.btn-table-focus').addEventListener('click', (e) => {
            e.stopPropagation();
            state.map.flyTo([t.Latitude, t.Longitude], 18, { duration: 1.0 });
            openTreeModal(t);
        });
        tbody.appendChild(tr);
    });
}

function updateMetricsAndUI() {
    const inside = state.analyzedTrees.filter(t => t.IsInsideCorridor);
    const crit = state.analyzedTrees.filter(t => t.RiskLevel === 'CRITICO').length;
    const highMed = state.analyzedTrees.filter(t => t.RiskLevel === 'ALTO' || t.RiskLevel === 'MEDIO').length;
    const safe = state.analyzedTrees.filter(t => t.RiskLevel === 'BAJO').length;

    document.getElementById('kpiTotal').textContent = inside.length;
    document.getElementById('kpiCritical').textContent = crit;
    document.getElementById('kpiMedium').textContent = highMed;
    document.getElementById('kpiSafe').textContent = state.analyzedTrees.filter(t => t.IsInsideCorridor && t.RiskLevel === 'MEDIO').length;

    document.getElementById('countAll').textContent = state.analyzedTrees.length;
    document.getElementById('countCrit').textContent = crit;
    document.getElementById('countMed').textContent = highMed;
    document.getElementById('countLow').textContent = safe;
}

function openTreeModal(tree) {
    document.getElementById('modalTreeId').textContent = tree.Id;
    document.getElementById('modalTreeSpecies').textContent = tree.Species;
    document.getElementById('modalTreeDist').textContent = `${tree.DistanceToCableM} m`;
    document.getElementById('modalTreeRisk').textContent = tree.RiskLevel;
    document.getElementById('modalTreeRisk').style.color = tree.RiskColor;
    document.getElementById('modalTreeHeight').textContent = `${tree.HeightM} m`;
    document.getElementById('modalTreeCrown').textContent = `${tree.CrownDiameterM} m`;
    document.getElementById('modalTreeCoords').textContent = `${tree.Latitude.toFixed(6)}, ${tree.Longitude.toFixed(6)}`;
    document.getElementById('modalTreeConf').textContent = `${tree.ConfidencePct}%`;
    document.getElementById('modalTreeAction').textContent = tree.RecommendedAction;

    const modal = document.getElementById('treeModal');
    modal.style.display = 'flex';

    document.getElementById('btnFocusMapFromModal').onclick = () => {
        modal.style.display = 'none';
        state.map.flyTo([tree.Latitude, tree.Longitude], 19, { duration: 1.0 });
    };
}

function runSimulatedScan() {
    const progressBox = document.getElementById('scanProgressBox');
    const progressBar = document.getElementById('scanProgressBar');
    const statusText = document.getElementById('scanStatusText');
    const percentText = document.getElementById('scanPercentText');
    const btnRun = document.getElementById('btnRunAnalysis');

    btnRun.disabled = true;
    progressBox.style.display = 'flex';
    progressBar.style.width = '0%';

    const phases = [
        { pct: 25, text: "1/4 Cargando Ortomosaico y Traza KMZ en C#..." },
        { pct: 50, text: `2/4 Generando Servidumbre de ${state.corridorWidthM}m con NetTopologySuite...` },
        { pct: 75, text: "3/4 Detectando Copas de Árboles (AI Engine)..." },
        { pct: 100, text: "4/4 Calculando Distancias y Clasificando Riesgos..." }
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
            setTimeout(() => {
                progressBox.style.display = 'none';
                btnRun.disabled = false;
                showToast(`Análisis completado en C#: ${state.analyzedTrees.filter(t => t.IsInsideCorridor).length} árboles en servidumbre.`);
            }, 500);
        }
    }, 350);
}

function showToast(msg) {
    const toast = document.getElementById('toastBox');
    document.getElementById('toastMessage').textContent = msg;
    toast.classList.add('show');
    setTimeout(() => { toast.classList.remove('show'); }, 3500);
}
