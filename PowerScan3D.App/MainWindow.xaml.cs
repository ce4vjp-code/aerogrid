using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using NetTopologySuite.Geometries;
using PowerScan3D.App.Core;
using PowerScan3D.App.Models;

namespace PowerScan3D.App;

public partial class MainWindow : Window
{
    private MissionModel _currentMission = MockDataService.GetDefaultMission();
    private List<Coordinate> _currentLineCoords = new();
    private List<List<Coordinate>> _currentLineSegments = new();
    private List<TreeModel> _currentTrees = new();
    private double _currentCorridorWidthM = 20.0;
    private readonly GisEngine _gisEngine = new(20.0);

    // Estado GeoTIFF Real
    private GeoTiffMetadata? _currentTiffMeta = null;
    private string _currentTiffPath = string.Empty;
    private string _currentDsmPath = string.Empty;
    private string _currentDtmPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string webAssetsDir = Path.Combine(appDir, "Assets", "Web");

            if (!Directory.Exists(webAssetsDir))
            {
                webAssetsDir = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "Assets", "Web"));
            }

            if (Directory.Exists(webAssetsDir))
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "powerscan.local",
                    webAssetsDir,
                    CoreWebView2HostResourceAccessKind.Allow
                );
                webView.CoreWebView2.Navigate("https://powerscan.local/index.html");
            }
            else
            {
                MessageBox.Show($"No se encontró la carpeta de recursos web en: {webAssetsDir}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al inicializar WebView2: {ex.Message}", "Error Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionElem)) return;
            string action = actionElem.GetString() ?? "";

            switch (action)
            {
                case "init":
                    HandleInit();
                    break;

                case "recalculate_corridor":
                    double newWidth = root.GetProperty("corridor_width_m").GetDouble();
                    HandleRecalculateCorridor(newWidth);
                    break;

                case "open_kmz_dialog":
                    HandleOpenKmzDialog();
                    break;

                case "load_sample_kmz":
                    HandleLoadSampleKmz();
                    break;

                case "open_geotiff_dialog":
                    HandleOpenGeoTiffDialog();
                    break;

                case "open_drone_photos_dialog":
                    HandleOpenDronePhotosDialog();
                    break;

                case "open_telemetry_file_dialog":
                    HandleOpenTelemetryFileDialog();
                    break;

                case "load_flight_telemetry":
                    string flightFolder = root.TryGetProperty("folder_path", out var flElem) ? flElem.GetString() ?? "" : "";
                    HandleLoadFlightTelemetry(flightFolder);
                    break;

                case "get_drone_photo_preview":
                    string pPath = root.TryGetProperty("photo_path", out var ppElem) ? ppElem.GetString() ?? "" : "";
                    HandleGetPhotoPreview(pPath);
                    break;

                case "start_photogrammetry_processing":
                    HandleStartPhotogrammetry(root);
                    break;

                case "start_nodeodm_processing":
                    HandleStartNodeOdm(root);
                    break;

                case "clear_workspace":
                    HandleClearWorkspace();
                    break;

                case "run_real_analysis":
                    int sensitivity = root.TryGetProperty("sensitivity", out var sElem) ? sElem.GetInt32() : 4;
                    HandleRunRealAnalysis(sensitivity);
                    break;

                case "delete_tree":
                    string deleteId = root.GetProperty("tree_id").GetString() ?? "";
                    HandleDeleteTree(deleteId);
                    break;

                case "add_manual_tree":
                    double lat = root.GetProperty("lat").GetDouble();
                    double lon = root.GetProperty("lon").GetDouble();
                    HandleAddManualTree(lat, lon);
                    break;
                    
                case "update_tree_height":
                    string updateId = root.GetProperty("tree_id").GetString() ?? "";
                    double newHeight = root.GetProperty("height_m").GetDouble();
                    HandleUpdateTreeHeight(updateId, newHeight);
                    break;

                case "export_pdf":
                    HandleExportPdf();
                    break;

                case "export_csv":
                    HandleExportCsv();
                    break;

                case "get_library_catalog":
                    SendToJs("library_catalog_updated", LibraryService.GetCatalog());
                    break;

                case "open_library_folder":
                    LibraryService.OpenFolderInExplorer();
                    break;

                case "load_library_file":
                    string libPath = root.TryGetProperty("file_path", out var lpElem) ? lpElem.GetString() ?? "" : "";
                    string libCat = root.TryGetProperty("category", out var lcElem) ? lcElem.GetString() ?? "" : "";
                    HandleLoadLibraryFile(libPath, libCat);
                    break;
            }
        }
        catch (Exception ex)
        {
            SendToJs("error", new { message = ex.Message });
        }
    }

    private void HandleInit()
    {
        _currentMission = new MissionModel { Name = "Espacio de Trabajo Limpio", Id = "PROY-01" };
        _currentLineCoords = new List<Coordinate>();
        _currentLineSegments = new List<List<Coordinate>>();
        _currentTrees = new List<TreeModel>();

        SendToJs("init_response", new
        {
            mission = _currentMission,
            trees = _currentTrees,
            corridor_polygon = new List<List<double[]>>(),
            corridor_width_m = _currentCorridorWidthM,
            library_catalog = LibraryService.GetCatalog()
        });
    }

    private void HandleRecalculateCorridor(double widthM)
    {
        _currentCorridorWidthM = widthM;
        _gisEngine.CorridorWidthM = widthM;

        if (!_currentLineSegments.Any())
        {
            if (_currentLineCoords.Count < 2)
                _currentLineCoords = MockDataService.GetMissionCoordinates(_currentMission);
            _currentLineSegments = new List<List<Coordinate>> { _currentLineCoords };
        }

        foreach (var t in _currentTrees)
        {
            _gisEngine.AnalyzeTreeMultiSegment(t, _currentLineSegments, widthM);
        }

        var corridorPolygons = _gisEngine.GenerateMultiCorridorPolygons(_currentLineSegments, widthM);

        SendToJs("corridor_updated", new
        {
            trees = _currentTrees,
            corridor_polygon = corridorPolygons,
            corridor_width_m = _currentCorridorWidthM
        });
    }

    private void HandleLoadLibraryFile(string filePath, string category)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            SendToJs("error", new { message = "El archivo de la biblioteca no se encuentra disponible." });
            return;
        }

        switch (category.ToLowerInvariant())
        {
            case "kmz":
            case "kml":
                LoadKmzFromPaths(new[] { filePath });
                break;
            case "geotiff":
                LoadGeoTiffFromPath(filePath);
                break;
            case "telemetry":
                HandleLoadFlightTelemetry(filePath);
                break;
        }
    }

    private void HandleOpenKmzDialog()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Seleccionar Archivo(s) KMZ o KML (Puedes seleccionar varios a la vez con Ctrl)",
            Filter = "Archivos KMZ / KML (*.kmz;*.kml)|*.kmz;*.kml|Todos los archivos (*.*)|*.*",
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            LoadKmzFromPaths(openFileDialog.FileNames);
        }
    }

    private void LoadKmzFromPaths(string[] filePaths)
    {
        try
        {
            var parsedResults = new List<KmzParseResult>();

            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath)) continue;

                // Guardar en la biblioteca persistente
                LibraryService.SaveToLibrary(filePath, "kmz");

                byte[] fileBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                string kmlXml = KmzParser.ExtractKmlContent(fileBytes, fileName);
                var pr = KmzParser.ParseKml(kmlXml, fileName);
                parsedResults.Add(pr);
            }

            if (!parsedResults.Any()) return;

            var parseResult = KmzParser.MergeResults(parsedResults);

            _currentLineSegments = parseResult.AllLineSegments;
            if (parseResult.LineCoordinates.Any())
            {
                _currentLineCoords = parseResult.LineCoordinates;
            }
            if (parseResult.Towers.Any())
            {
                _currentMission.Towers = parseResult.Towers;
            }

            if (!_currentLineSegments.Any() && _currentLineCoords.Count >= 2)
            {
                _currentLineSegments = new List<List<Coordinate>> { _currentLineCoords };
            }

            var corridorPolygons = _gisEngine.GenerateMultiCorridorPolygons(_currentLineSegments, _currentCorridorWidthM);

            // Reevaluar árboles existentes sobre los tramos reales
            if (_currentLineSegments.Any())
            {
                foreach (var t in _currentTrees)
                {
                    _gisEngine.AnalyzeTreeMultiSegment(t, _currentLineSegments, _currentCorridorWidthM);
                }
            }

            SendToJs("kmz_loaded", new
            {
                filename = parseResult.FileName,
                file_count = parsedResults.Count,
                feature_count = parseResult.FeatureCount,
                line_count = parseResult.AllLineSegments.Count,
                tower_count = parseResult.Towers.Count,
                geojson = JsonDocument.Parse(parseResult.GeoJson).RootElement,
                corridor_polygon = corridorPolygons,
                corridor_width_m = _currentCorridorWidthM,
                trees = _currentTrees
            });

            // Notificar catálogo actualizado de biblioteca
            SendToJs("library_catalog_updated", LibraryService.GetCatalog());
        }
        catch (Exception ex)
        {
            SendToJs("error", new { message = $"Error al procesar capas KML/KMZ: {ex.Message}" });
        }
    }

    private void HandleLoadSampleKmz()
    {
        string samplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "LAT_220kV_Muestra.kmz");
        if (!File.Exists(samplePath))
        {
            KmzParser.CreateSampleKmz(samplePath);
        }

        byte[] fileBytes = File.ReadAllBytes(samplePath);
        string kmlXml = KmzParser.ExtractKmlContent(fileBytes, "LAT_220kV_Muestra.kmz");
        var parseResult = KmzParser.ParseKml(kmlXml, "LAT_220kV_Muestra.kmz");

        _currentLineSegments = parseResult.AllLineSegments;
        _currentLineCoords = parseResult.LineCoordinates;
        if (parseResult.Towers.Any()) _currentMission.Towers = parseResult.Towers;

        if (!_currentLineSegments.Any() && _currentLineCoords.Count >= 2)
        {
            _currentLineSegments = new List<List<Coordinate>> { _currentLineCoords };
        }

        var corridorPolygons = _gisEngine.GenerateMultiCorridorPolygons(_currentLineSegments, _currentCorridorWidthM);

        SendToJs("kmz_loaded", new
        {
            filename = "LAT_220kV_Muestra.kmz",
            geojson = JsonDocument.Parse(parseResult.GeoJson).RootElement,
            corridor_polygon = corridorPolygons,
            corridor_width_m = _currentCorridorWidthM,
            trees = _currentTrees
        });
    }

    private void HandleOpenGeoTiffDialog()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Seleccionar Ortofoto GeoTIFF",
            Filter = "Imágenes GeoTIFF (*.tif;*.tiff)|*.tif;*.tiff|Todos los archivos (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            LoadGeoTiffFromPath(openFileDialog.FileName);
        }
    }

    private void LoadGeoTiffFromPath(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            // Guardar en la biblioteca persistente
            LibraryService.SaveToLibrary(filePath, "geotiff");

            _currentTiffPath = filePath;
            _currentTiffMeta = GeoTiffService.LoadGeoTiff(_currentTiffPath);

            // Búsqueda inteligente de Modelos de Elevación 3D (DSM/DTM)
            string dir = Path.GetDirectoryName(filePath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(filePath).Replace("_Ortofoto_HD", "");
            string possibleDsm = Path.Combine(dir, $"{baseName}_DSM.tif");
            string possibleDtm = Path.Combine(dir, $"{baseName}_DTM.tif");
            
            _currentDsmPath = File.Exists(possibleDsm) ? possibleDsm : string.Empty;
            _currentDtmPath = File.Exists(possibleDtm) ? possibleDtm : string.Empty;

            SendToJs("geotiff_loaded", new
            {
                filename = _currentTiffMeta.FileName,
                minLat = _currentTiffMeta.MinLat,
                maxLat = _currentTiffMeta.MaxLat,
                minLon = _currentTiffMeta.MinLon,
                maxLon = _currentTiffMeta.MaxLon,
                gsd_cm = Math.Round(_currentTiffMeta.GsdMeters * 100, 1),
                crs = _currentTiffMeta.CrsName,
                epsg = _currentTiffMeta.EpsgCode,
                width = _currentTiffMeta.Width,
                height = _currentTiffMeta.Height,
                image_base64 = _currentTiffMeta.ImageBase64,
                debug_info = _currentTiffMeta.DebugInfo,
                has_dsm = !string.IsNullOrEmpty(_currentDsmPath),
                has_dtm = !string.IsNullOrEmpty(_currentDtmPath)
            });

            // Notificar catálogo actualizado de biblioteca
            SendToJs("library_catalog_updated", LibraryService.GetCatalog());
        }
        catch (Exception ex)
        {
            SendToJs("error", new { message = $"Error al cargar GeoTIFF: {ex.Message}" });
        }
    }

    private void HandleRunRealAnalysis(int sensitivity = 3)
    {
        if (_currentTiffMeta != null && File.Exists(_currentTiffPath))
        {
            if (_currentLineSegments == null || _currentLineSegments.Count == 0)
            {
                var mockCoords = MockDataService.GetMissionCoordinates(_currentMission);
                _currentLineSegments = new List<List<Coordinate>> { mockCoords };
            }

            // Detección real multi-espectral en la ortofoto y extracción de altitud (Z) desde el DSM/DTM
            var realTrees = GeoTiffService.AnalyzeRealVegetation(
                _currentTiffPath, 
                _currentTiffMeta, 
                _currentLineSegments, 
                _currentCorridorWidthM,
                sensitivity,
                _currentDsmPath,
                _currentDtmPath
            );

            if (realTrees.Any())
            {
                _currentTrees = realTrees;
            }

            var corridorPolygon = _gisEngine.GenerateCorridorPolygon(_currentLineCoords, _currentCorridorWidthM);

            SendToJs("real_analysis_completed", new
            {
                trees = _currentTrees,
                corridor_polygon = corridorPolygon,
                corridor_width_m = _currentCorridorWidthM,
                count = _currentTrees.Count
            });
        }
        else
        {
            // Ejecución sobre la misión actual
            if (_currentLineCoords.Count < 2)
                _currentLineCoords = MockDataService.GetMissionCoordinates(_currentMission);

            _currentTrees = MockDataService.GenerateTrees(_currentMission, _currentCorridorWidthM);
            var corridorPolygon = _gisEngine.GenerateCorridorPolygon(_currentLineCoords, _currentCorridorWidthM);

            SendToJs("real_analysis_completed", new
            {
                trees = _currentTrees,
                corridor_polygon = corridorPolygon,
                corridor_width_m = _currentCorridorWidthM,
                count = _currentTrees.Count
            });
        }
    }

    private void HandleExportPdf()
    {
        string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string outputDir = Path.Combine(downloadsDir, "PowerScan3D-CSharp", "output");
        Directory.CreateDirectory(outputDir);

        string filename = $"Informe_Tecnico_Servidumbre_{Convert.ToInt32(_currentCorridorWidthM)}m_{_currentMission.Id}.pdf";
        string fullPath = Path.Combine(outputDir, filename);

        PdfReportService.GenerateReport(_currentMission, _currentTrees, _currentCorridorWidthM, fullPath);

        SendToJs("toast", new { message = $"Informe PDF generado con éxito en C#: {filename}" });

        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
    }

    private void HandleExportCsv()
    {
        string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string outputDir = Path.Combine(downloadsDir, "PowerScan3D-CSharp", "output");
        Directory.CreateDirectory(outputDir);

        string filename = $"Reporte_Vegetacion_{Convert.ToInt32(_currentCorridorWidthM)}m_{_currentMission.Id}.csv";
        string fullPath = Path.Combine(outputDir, filename);

        using (var sw = new StreamWriter(fullPath, false, System.Text.Encoding.UTF8))
        {
            sw.WriteLine("ID,Latitud,Longitud,Distancia_Cable_m,En_Servidumbre,Riesgo,Accion_Recomendada,Altura_m,Copa_m,Especie,Confianza_pct,Foto_Origen");
            foreach (var t in _currentTrees)
            {
                sw.WriteLine($"{t.Id},{t.Latitude:F6},{t.Longitude:F6},{t.DistanceToCableM},{ (t.IsInsideCorridor ? "SI" : "NO") },{t.RiskLevel},\"{t.RecommendedAction}\",{t.HeightM},{t.CrownDiameterM},\"{t.Species}\",{t.ConfidencePct},{t.SourcePhoto}");
            }
        }

        SendToJs("toast", new { message = $"Archivo CSV exportado exitosamente: {filename}" });
    }

    private void HandleOpenDronePhotosDialog()
    {
        var openFolderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar Carpeta con Fotos Crudas de Dron (.JPG / .DNG con EXIF GPS)"
        };

        if (openFolderDialog.ShowDialog() == true)
        {
            HandleLoadFlightTelemetry(openFolderDialog.FolderName);
        }
    }

    private void HandleOpenTelemetryFileDialog()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Seleccionar Archivo de Telemetría o Disparos de Dron (.MRK, .CSV, .TXT)",
            Filter = "Archivos de Disparo / Telemetría (*.MRK;*.csv;*.txt)|*.MRK;*.csv;*.txt|Todos los archivos (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            HandleLoadFlightTelemetry(openFileDialog.FileName);
        }
    }

    private void HandleLoadFlightTelemetry(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            HandleOpenTelemetryFileDialog();
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            SendToJs("error", new { message = "No se encontró el archivo o carpeta de telemetría seleccionado." });
            return;
        }

        var flightMeta = WebOdmService.ParseDjiFlightTelemetry(path);

        if (flightMeta.Shots.Count == 0)
        {
            SendToJs("error", new { message = "No se encontraron coordenadas de disparo válidas en el archivo seleccionado." });
            return;
        }

        SendToJs("drone_flight_loaded", new
        {
            folder_path = flightMeta.FolderPath,
            folder_name = flightMeta.FlightName,
            photo_count = flightMeta.PhotoCount,
            minLat = flightMeta.MinLat,
            maxLat = flightMeta.MaxLat,
            minLon = flightMeta.MinLon,
            maxLon = flightMeta.MaxLon,
            avg_alt_m = flightMeta.AvgAltitudeM,
            shots = flightMeta.Shots,
            trajectory = flightMeta.FlightTrajectory.Select(c => new[] { c.X, c.Y }).ToList()
        });
    }

    private void HandleGetPhotoPreview(string photoPath)
    {
        if (!File.Exists(photoPath))
        {
            SendToJs("drone_photo_preview_ready", new { success = false, message = "Archivo no encontrado" });
            return;
        }

        string base64 = WebOdmService.GetPhotoThumbnailBase64(photoPath, 900);
        SendToJs("drone_photo_preview_ready", new
        {
            success = true,
            photo_name = Path.GetFileName(photoPath),
            photo_path = photoPath,
            image_base64 = base64
        });
    }

    private void HandleStartNodeOdm(JsonElement root)
    {
        string folder = root.TryGetProperty("folder_path", out var fElem) ? fElem.GetString() ?? "" : "";
        string preset = root.TryGetProperty("preset", out var pElem) ? pElem.GetString() ?? "corridor" : "corridor";

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            SendToJs("error", new { message = "La carpeta seleccionada no es válida." });
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var client = new NodeOdmClient();
                SendToJs("photogrammetry_progress", new { percent = 10, stage = "CONECTANDO", message = "Comprobando contenedor NodeODM en localhost:3000..." });
                
                bool isUp = await client.PingAsync();
                if (!isUp)
                {
                    SendToJs("error", new { message = "No se pudo conectar a NodeODM en localhost:3000.\n\nPor favor, asegúrate de tener Docker Desktop instalado y en ejecución, y de haber iniciado el contenedor con:\ndocker-compose up -d" });
                    return;
                }

                SendToJs("photogrammetry_progress", new { percent = 20, stage = "PREPARANDO", message = "Escaneando fotografías del dron..." });
                var photos = WebOdmService.ScanDronePhotos(folder);
                if (photos.Count == 0)
                {
                    SendToJs("error", new { message = "No se encontraron fotos (.JPG, .TIF) en la carpeta." });
                    return;
                }

                SendToJs("photogrammetry_progress", new { percent = 30, stage = "SUBIENDO", message = $"Enviando {photos.Count} fotos a NodeODM..." });
                
                string taskName = $"PowerScan3D_{DateTime.Now:yyyyMMdd_HHmmss}";
                string uuid = await client.CreateTaskAsync(taskName, photos, preset);

                SendToJs("photogrammetry_progress", new { percent = 50, stage = "PROCESANDO", message = "Fotos subidas. Fotogrametría 3D en progreso (esto puede tardar horas)..." });

                // Polling Loop
                bool isDone = false;
                while (!isDone)
                {
                    await Task.Delay(5000);
                    var info = await client.GetTaskInfoAsync(uuid);
                    
                    if (info.status.code == 30) // Failed
                    {
                        SendToJs("error", new { message = $"El procesamiento en NodeODM falló: {info.error}" });
                        return;
                    }
                    else if (info.status.code == 50) // Canceled
                    {
                        SendToJs("error", new { message = "El procesamiento en NodeODM fue cancelado." });
                        return;
                    }
                    else if (info.status.code == 40) // Completed
                    {
                        isDone = true;
                    }
                    else
                    {
                        // Update progress (NodeODM progress is usually 0-100 for the processing phase)
                        int progressInt = (int)Math.Round(info.progress);
                        int totalPct = 50 + (progressInt / 2);
                        SendToJs("photogrammetry_progress", new { percent = totalPct, stage = "PROCESANDO", message = $"Generando nube de puntos y ortofoto ({progressInt}%)..." });
                    }
                }

                SendToJs("photogrammetry_progress", new { percent = 95, stage = "DESCARGANDO", message = "Proceso completado. Descargando Ortofoto, DSM y DTM..." });
                
                string libTiffDir = Path.Combine(LibraryService.GetLibraryRoot(), "ortofotos_geotiff");
                Directory.CreateDirectory(libTiffDir);
                string orthoPath = Path.Combine(libTiffDir, $"{taskName}_Ortofoto_HD.tif");
                string dsmPath = Path.Combine(libTiffDir, $"{taskName}_DSM.tif");
                string dtmPath = Path.Combine(libTiffDir, $"{taskName}_DTM.tif");

                await client.DownloadOrthophotoAsync(uuid, orthoPath);
                
                try {
                    await client.DownloadDsmAsync(uuid, dsmPath);
                    await client.DownloadDtmAsync(uuid, dtmPath);
                } catch (Exception ex) {
                    Console.WriteLine($"Error descargando DEMs: {ex.Message}");
                }

                SendToJs("photogrammetry_progress", new { percent = 100, stage = "COMPLETADO", message = "Mapas 3D guardados en la biblioteca." });
                SendToJs("photogrammetry_completed", new { tiffPath = orthoPath, dsmPath = dsmPath, dtmPath = dtmPath });
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Error en NodeODM: {ex.Message}" });
            }
        });
    }

    private void HandleStartPhotogrammetry(JsonElement root)
    {
        string folder = root.TryGetProperty("folder_path", out var fElem) ? fElem.GetString() ?? "" : "";
        string preset = root.TryGetProperty("preset", out var pElem) ? pElem.GetString() ?? "corridor" : "corridor";

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            SendToJs("error", new { message = "La carpeta de fotografías de dron seleccionada no es válida o no existe." });
            return;
        }

        Task.Run(() =>
        {
            try
            {
                string libTiffDir = Path.Combine(LibraryService.GetLibraryRoot(), "ortofotos_geotiff");
                Directory.CreateDirectory(libTiffDir);

                SendToJs("photogrammetry_progress", new
                {
                    percent = 5,
                    stage = "INICIANDO",
                    message = "Leyendo telemetría RTK cinemática y fotos aéreas..."
                });

                // 1. Extraer telemetría cinemática real de los archivos DJI .MRK o EXIF
                var flightMeta = WebOdmService.ParseDjiFlightTelemetry(folder);

                if (flightMeta.Shots.Count == 0)
                {
                    SendToJs("error", new { message = "No se encontraron fotos o archivo de telemetría válido (.MRK, .CSV) en la carpeta seleccionada." });
                    return;
                }

                // 2. Generar el GeoTIFF fotogramétrico dedicado para este vuelo con reporte de progreso
                string outputTiff = WebOdmService.GenerateFlightOrthophoto(flightMeta, libTiffDir, preset, (pct, msg) =>
                {
                    SendToJs("photogrammetry_progress", new
                    {
                        percent = pct,
                        stage = "PROCESANDO",
                        message = msg
                    });
                });

                if (File.Exists(outputTiff))
                {
                    _currentTiffPath = outputTiff;
                    _currentTiffMeta = GeoTiffService.LoadGeoTiff(_currentTiffPath);

                    // Enviar Ortofoto dedicada a JavaScript (sin análisis automático)
                    SendToJs("geotiff_loaded", new
                    {
                        filename = _currentTiffMeta.FileName,
                        minLat = _currentTiffMeta.MinLat,
                        maxLat = _currentTiffMeta.MaxLat,
                        minLon = _currentTiffMeta.MinLon,
                        maxLon = _currentTiffMeta.MaxLon,
                        gsd_cm = Math.Round(_currentTiffMeta.GsdMeters * 100, 1),
                        crs = _currentTiffMeta.CrsName,
                        epsg = _currentTiffMeta.EpsgCode,
                        width = _currentTiffMeta.Width,
                        height = _currentTiffMeta.Height,
                        image_base64 = _currentTiffMeta.ImageBase64,
                        debug_info = _currentTiffMeta.DebugInfo
                    });

                    // Notificar actualización de biblioteca
                    SendToJs("library_catalog_updated", LibraryService.GetCatalog());
                }

                SendToJs("photogrammetry_completed", new
                {
                    status = "COMPLETED",
                    output_tiff = outputTiff,
                    preset = preset,
                    message = $"Ortomosaico fotogramétrico generado con éxito y guardado en la biblioteca: {Path.GetFileName(outputTiff)}"
                });
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Error durante la generación de la ortofoto: {ex.Message}" });
            }
        });
    }

    private void HandleClearWorkspace()
    {
        _currentTiffPath = "";
        _currentTiffMeta = null;
        _currentLineCoords.Clear();
        _currentTrees.Clear();

        SendToJs("workspace_cleared", new
        {
            message = "Espacio de trabajo limpiado. Foto GeoTIFF y trazado KMZ eliminados de la vista."
        });
    }

    private void HandleDeleteTree(string treeId)
    {
        var tree = _currentTrees.FirstOrDefault(t => t.Id == treeId);
        if (tree != null)
        {
            _currentTrees.Remove(tree);
            SendToJs("refresh_trees", new { trees = _currentTrees });
        }
    }

    private void HandleAddManualTree(double lat, double lon)
    {
        string newId = $"ARB-MAN-{(new Random().Next(1000, 9999))}";
        var manualTree = new TreeModel
        {
            Id = newId,
            Latitude = lat,
            Longitude = lon,
            HeightM = 15.0,
            CrownDiameterM = 3.0,
            Species = "Manual (No Identificado)",
            ConfidencePct = 100.0,
            SourcePhoto = "Manual"
        };

        _gisEngine.AnalyzeTreeMultiSegment(manualTree, _currentLineSegments, _currentCorridorWidthM);
        
        _currentTrees.Add(manualTree);
        SendToJs("refresh_trees", new { trees = _currentTrees });
    }

    private void HandleUpdateTreeHeight(string treeId, double newHeightM)
    {
        var tree = _currentTrees.FirstOrDefault(t => t.Id == treeId);
        if (tree != null)
        {
            tree.HeightM = newHeightM;
            _gisEngine.AnalyzeTreeMultiSegment(tree, _currentLineSegments, _currentCorridorWidthM);
            SendToJs("refresh_trees", new { trees = _currentTrees });
        }
    }

    private void SendToJs(string action, object payload)
    {
        try
        {
            var msg = new { action, payload };
            string json = JsonSerializer.Serialize(msg);

            if (Dispatcher.CheckAccess())
            {
                webView?.CoreWebView2?.PostWebMessageAsJson(json);
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    webView?.CoreWebView2?.PostWebMessageAsJson(json);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SendToJs Error] {ex.Message}");
        }
    }
}