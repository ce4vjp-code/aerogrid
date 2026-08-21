using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using PowerScan3D.App.Models;
using SkiaSharp;

namespace PowerScan3D.App.Core;

public class DroneCameraShot
{
    public int Index { get; set; }
    public string PhotoFileName { get; set; } = string.Empty;
    public string PhotoFullPath { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeM { get; set; }
    public string Quality { get; set; } = "RTK Fix";
    public string GpsTime { get; set; } = string.Empty;
}

public class DroneFlightMetadata
{
    public string FolderPath { get; set; } = string.Empty;
    public string FlightName { get; set; } = string.Empty;
    public int PhotoCount { get; set; }
    public double MinLat { get; set; }
    public double MaxLat { get; set; }
    public double MinLon { get; set; }
    public double MaxLon { get; set; }
    public double AvgAltitudeM { get; set; }
    public List<DroneCameraShot> Shots { get; set; } = new();
    public List<Coordinate> FlightTrajectory { get; set; } = new();
}

public class WebOdmService
{
    public static List<string> ScanDronePhotos(string folderPath)
    {
        var files = new List<string>();
        if (!Directory.Exists(folderPath)) return files;

        var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".dng", ".tif", ".tiff"
        };

        try
        {
            foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (validExtensions.Contains(Path.GetExtension(file)))
                {
                    files.Add(file);
                }
            }

            // Si no encontró en la carpeta raíz, buscar recursivamente
            if (files.Count == 0)
            {
                foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                {
                    if (validExtensions.Contains(Path.GetExtension(file)))
                    {
                        files.Add(file);
                    }
                }
            }
        }
        catch
        {
            // Continuar con lo que se haya recolectado
        }

        return files.OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Extrae todos los disparos individuales de cámara desde un archivo (.MRK, .CSV, .TXT) o carpeta de vuelo
    /// </summary>
    public static DroneFlightMetadata ParseDjiFlightTelemetry(string path)
    {
        var meta = new DroneFlightMetadata();

        string folderPath = "";
        string targetFile = "";

        if (File.Exists(path))
        {
            targetFile = path;
            folderPath = Path.GetDirectoryName(path) ?? "";
            meta.FlightName = Path.GetFileNameWithoutExtension(path);
        }
        else if (Directory.Exists(path))
        {
            folderPath = path;
            meta.FlightName = Path.GetFileName(path);
            string[] mrkFiles = Directory.GetFiles(folderPath, "*_D.MRK", SearchOption.TopDirectoryOnly);
            if (mrkFiles.Length == 0)
                mrkFiles = Directory.GetFiles(folderPath, "*.MRK", SearchOption.TopDirectoryOnly);
            if (mrkFiles.Length == 0)
                mrkFiles = Directory.GetFiles(folderPath, "*.csv", SearchOption.TopDirectoryOnly);

            if (mrkFiles.Length > 0) targetFile = mrkFiles[0];
        }
        else
        {
            return meta;
        }

        meta.FolderPath = folderPath;
        var photoPaths = !string.IsNullOrEmpty(folderPath) ? ScanDronePhotos(folderPath) : new List<string>();
        meta.PhotoCount = photoPaths.Count;

        // 1. Parser de archivo DJI .MRK
        if (!string.IsNullOrEmpty(targetFile) && targetFile.EndsWith(".MRK", StringComparison.OrdinalIgnoreCase))
        {
            var regex = new Regex(@"^(\d+)\s+([\d\.]+)\s+\[\d+\]\s+[^\t]+\t[^\t]+\t[^\t]+\t(-?\d+\.\d+),Lat\s+(-?\d+\.\d+),Lon\s+(\d+\.\d+),Ellh(?:\s+[^\t]+\t(\d+),Q)?", RegexOptions.Compiled);
            var lines = File.ReadAllLines(targetFile);

            foreach (var line in lines)
            {
                var match = regex.Match(line.Trim());
                if (match.Success)
                {
                    int idx = int.Parse(match.Groups[1].Value);
                    string gpsTime = match.Groups[2].Value;
                    double lat = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    double lon = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                    double alt = double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
                    string qCode = match.Groups[6].Success ? match.Groups[6].Value : "16";

                    string matchingPhoto = "";
                    string photoName = $"DJI_{idx:D4}_V.JPG";
                    string? foundPhoto = photoPaths.FirstOrDefault(p => 
                        Path.GetFileName(p).Contains($"_{idx:D4}_", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).Contains($"_{idx:D4}.", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).Contains($"_{idx:D3}_", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).Contains($"_{idx:D3}.", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).Contains($"_{idx}_", StringComparison.OrdinalIgnoreCase));

                    // Fallback a correspondencia por orden secuencial de archivos en la carpeta
                    if (foundPhoto == null && idx - 1 >= 0 && idx - 1 < photoPaths.Count)
                    {
                        foundPhoto = photoPaths[idx - 1];
                    }

                    if (foundPhoto != null)
                    {
                        matchingPhoto = foundPhoto;
                        photoName = Path.GetFileName(foundPhoto);
                    }

                    var shot = new DroneCameraShot
                    {
                        Index = idx,
                        PhotoFileName = photoName,
                        PhotoFullPath = matchingPhoto,
                        Latitude = lat,
                        Longitude = lon,
                        AltitudeM = Math.Round(alt, 2),
                        Quality = qCode == "16" ? "RTK Fix (Centimétrico)" : "GPS Autónomo",
                        GpsTime = gpsTime
                    };

                    meta.Shots.Add(shot);
                    meta.FlightTrajectory.Add(new Coordinate(lon, lat));
                }
            }
        }
        // 2. Parser de archivo genérico .CSV / .TXT
        else if (!string.IsNullOrEmpty(targetFile) && (targetFile.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || targetFile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(targetFile);
            int idx = 1;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var tokens = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2)
                {
                    double lat = 0, lon = 0, alt = 199.0;
                    bool hasCoords = false;

                    for (int i = 0; i < tokens.Length - 1; i++)
                    {
                        if (double.TryParse(tokens[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val1) &&
                            double.TryParse(tokens[i + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val2))
                        {
                            if (Math.Abs(val1) <= 90 && Math.Abs(val2) <= 180 && (val2 < -60 || val1 < -60))
                            {
                                lat = Math.Abs(val1) <= 90 ? val1 : val2;
                                lon = Math.Abs(val1) <= 90 ? val2 : val1;
                                hasCoords = true;
                                if (i + 2 < tokens.Length && double.TryParse(tokens[i + 2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val3))
                                {
                                    alt = val3;
                                }
                                break;
                            }
                        }
                    }

                    if (hasCoords)
                    {
                        meta.Shots.Add(new DroneCameraShot
                        {
                            Index = idx,
                            PhotoFileName = $"Foto_{idx:D3}",
                            PhotoFullPath = "",
                            Latitude = lat,
                            Longitude = lon,
                            AltitudeM = Math.Round(alt, 2),
                            Quality = "CSV Telemetría"
                        });
                        meta.FlightTrajectory.Add(new Coordinate(lon, lat));
                        idx++;
                    }
                }
            }
        }

        if (meta.Shots.Count > 0)
        {
            meta.MinLat = meta.Shots.Min(s => s.Latitude);
            meta.MaxLat = meta.Shots.Max(s => s.Latitude);
            meta.MinLon = meta.Shots.Min(s => s.Longitude);
            meta.MaxLon = meta.Shots.Max(s => s.Longitude);
            meta.AvgAltitudeM = Math.Round(meta.Shots.Average(s => s.AltitudeM), 1);
            if (meta.PhotoCount == 0) meta.PhotoCount = meta.Shots.Count;
        }

        return meta;
    }

    /// <summary>
    /// Motor de Ortomosaico Voronoi v3.
    /// Usa teselación Voronoi verdadera (contra TODOS los disparos) para recortar,
    /// pero renderiza con DrawImage de SkiaSharp para calidad profesional.
    /// </summary>
    public static string GenerateFlightOrthophoto(DroneFlightMetadata flightMeta, string outputDir, string preset = "corridor", Action<int, string>? progressCallback = null)
    {
        string safeFlightName = string.IsNullOrEmpty(flightMeta.FlightName) ? "Vuelo_Dron" : flightMeta.FlightName;
        string outputPath = Path.Combine(outputDir, $"{safeFlightName}_Ortofoto.png");
        Directory.CreateDirectory(outputDir);

        progressCallback?.Invoke(3, "Inicializando motor de mosaico Voronoi v3...");

        var validShots = flightMeta.Shots.Where(s => !string.IsNullOrEmpty(s.PhotoFullPath) && File.Exists(s.PhotoFullPath)).ToList();
        if (validShots.Count == 0)
            throw new InvalidOperationException("No se encontraron fotografías .JPG para componer la ortofoto.");

        // --- Leer primera foto para determinar dimensiones reales ---
        int refPhotoW = 4, refPhotoH = 3;
        try
        {
            using var rs = File.OpenRead(validShots[0].PhotoFullPath);
            using var rb = SKBitmap.Decode(rs);
            if (rb != null) { refPhotoW = rb.Width; refPhotoH = rb.Height; }
        }
        catch { }

        // --- Constantes geográficas ---
        double centerLat = (flightMeta.MinLat + flightMeta.MaxLat) / 2.0;
        double cosLat = Math.Cos(centerLat * Math.PI / 180.0);
        double mPerDegLat = 111320.0;
        double mPerDegLon = 111320.0 * Math.Max(0.1, cosLat);

        // --- Huella en tierra: ancho generoso, aspect ratio real de la foto ---
        // Usamos una huella intencionalmente grande (120m) para evitar píxeles
        // fuera de rango. El recorte Voronoi limita lo que se muestra de cada foto.
        double footW, footH;
        if (refPhotoW >= refPhotoH)
        {
            // Landscape: eje ancho = Este-Oeste
            footW = 120.0;
            footH = 120.0 * refPhotoH / refPhotoW;
        }
        else
        {
            // Portrait: eje alto = Norte-Sur
            footH = 120.0;
            footW = 120.0 * refPhotoW / refPhotoH;
        }

        progressCallback?.Invoke(5, $"Foto ref: {refPhotoW}x{refPhotoH} — Huella: {footW:F0}m × {footH:F0}m...");

        // --- Filtrar disparos: mínimo 10m entre ellos ---
        var filteredShots = new List<DroneCameraShot>();
        DroneCameraShot? lastAdded = null;
        foreach (var s in validShots)
        {
            if (lastAdded == null) { filteredShots.Add(s); lastAdded = s; continue; }
            double dLat2 = (s.Latitude - lastAdded.Latitude) * mPerDegLat;
            double dLon2 = (s.Longitude - lastAdded.Longitude) * mPerDegLon;
            if (Math.Sqrt(dLat2 * dLat2 + dLon2 * dLon2) >= 10.0)
            {
                filteredShots.Add(s);
                lastAdded = s;
            }
        }
        if (filteredShots.Count < 5) filteredShots = validShots;
        int totalShots = filteredShots.Count;

        progressCallback?.Invoke(7, $"Seleccionados {totalShots} fotogramas clave de {validShots.Count} originales...");

        // --- Dimensiones del lienzo proporcionales al área real ---
        double marginLat = (footH * 0.55) / mPerDegLat;
        double marginLon = (footW * 0.55) / mPerDegLon;
        double minLat = flightMeta.MinLat - marginLat;
        double maxLat = flightMeta.MaxLat + marginLat;
        double minLon = flightMeta.MinLon - marginLon;
        double maxLon = flightMeta.MaxLon + marginLon;
        double lonSpan = maxLon - minLon;
        double latSpan = maxLat - minLat;
        if (lonSpan <= 1e-5) lonSpan = 0.001;
        if (latSpan <= 1e-5) latSpan = 0.001;

        double areaW_M = lonSpan * mPerDegLon;
        double areaH_M = latSpan * mPerDegLat;
        double aspect = areaW_M / areaH_M;

        // --- Se aumentó drásticamente la resolución para mejorar la definición (nitidez) ---
        int maxDim = preset switch { "fast" => 4096, "highres" => 12288, _ => 8192 };
        int targetWidth, targetHeight;
        if (aspect >= 1.0)
        {
            targetWidth = maxDim;
            targetHeight = Math.Max(1024, (int)(maxDim / aspect));
        }
        else
        {
            targetHeight = maxDim;
            targetWidth = Math.Max(1024, (int)(maxDim * aspect));
        }

        // --- Tamaño de la foto en el lienzo (píxeles) ---
        float drawW = (float)((footW / areaW_M) * targetWidth);
        float drawH = (float)((footH / areaH_M) * targetHeight);

        progressCallback?.Invoke(9, $"Lienzo {targetWidth}x{targetHeight} — Área {areaW_M:F0}m × {areaH_M:F0}m — Foto: {drawW:F0}x{drawH:F0}px...");

        // --- Pre-calcular centros en coordenadas del lienzo ---
        float[] cX = new float[totalShots];
        float[] cY = new float[totalShots];
        for (int i = 0; i < totalShots; i++)
        {
            cX[i] = (float)(((filteredShots[i].Longitude - minLon) / lonSpan) * targetWidth);
            cY[i] = (float)((1.0 - (filteredShots[i].Latitude - minLat) / latSpan) * targetHeight);
        }

        // --- Paso 1: Mapa de asignación Voronoi (grilla de 8×8 px) ---
        progressCallback?.Invoke(11, "Calculando teselación Voronoi contra todos los disparos...");
        int cellSz = 8;
        int gW = (targetWidth + cellSz - 1) / cellSz;
        int gH = (targetHeight + cellSz - 1) / cellSz;
        int[] assignMap = new int[gW * gH];

        for (int gy = 0; gy < gH; gy++)
        {
            float py = gy * cellSz + cellSz * 0.5f;
            for (int gx = 0; gx < gW; gx++)
            {
                float px = gx * cellSz + cellSz * 0.5f;
                int best = 0;
                float bestD = float.MaxValue;
                for (int si = 0; si < totalShots; si++)
                {
                    float dx = cX[si] - px;
                    float dy = cY[si] - py;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = si; }
                }
                assignMap[gy * gW + gx] = best;
            }
        }

        // Agrupar celdas por foto
        var photoCells = new List<int>[totalShots];
        for (int si = 0; si < totalShots; si++) photoCells[si] = new List<int>();
        for (int i = 0; i < assignMap.Length; i++) photoCells[assignMap[i]].Add(i);

        // --- Paso 2: Crear lienzo con SKSurface ---
        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(16, 24, 18, 255));

        progressCallback?.Invoke(15, $"Renderizando mosaico Voronoi ({totalShots} tomas)...");

        // --- Paso 3: Renderizar cada foto recortada a su celda Voronoi ---
        for (int si = 0; si < totalShots; si++)
        {
            if (photoCells[si].Count == 0) continue;
            var shot = filteredShots[si];
            int pct = 15 + (int)((si / (double)totalShots) * 72);

            try
            {
                using var strm = File.OpenRead(shot.PhotoFullPath);
                using var photoBmp = SKBitmap.Decode(strm);
                if (photoBmp == null) continue;

                // --- Construir región de recorte Voronoi ---
                using var clipRegion = new SKRegion();
                foreach (int cellIdx in photoCells[si])
                {
                    int gy = cellIdx / gW;
                    int gx = cellIdx % gW;
                    clipRegion.Op(
                        new SKRectI(gx * cellSz, gy * cellSz, (gx + 1) * cellSz, (gy + 1) * cellSz),
                        SKRegionOperation.Union);
                }

                // --- Calcular Orientación (Heading) de la Foto ---
                double dx = 0;
                double dy = 0;
                if (si < totalShots - 1)
                {
                    dx = filteredShots[si + 1].Longitude - shot.Longitude;
                    dy = filteredShots[si + 1].Latitude - shot.Latitude;
                }
                else if (si > 0)
                {
                    dx = shot.Longitude - filteredShots[si - 1].Longitude;
                    dy = shot.Latitude - filteredShots[si - 1].Latitude;
                }
                
                // Vector en el lienzo (Y crece hacia abajo)
                double vecX = dx * mPerDegLon;
                double vecY = -dy * mPerDegLat;
                float headingDeg = (float)(Math.Atan2(vecY, vecX) * 180.0 / Math.PI);
                float rotation = headingDeg + 90.0f; // 0 radianes = Este -> rotación +90 para que 'arriba' mire al Este

                // --- Posicionar la foto centrada en su coordenada GPS ---
                float cx = cX[si];
                float cy = cY[si];
                
                // El destRect se centra en (0,0) porque haremos Translate al centro
                var destRect = new SKRect(-drawW / 2f, -drawH / 2f, drawW / 2f, drawH / 2f);
                var srcRect = new SKRect(0, 0, photoBmp.Width, photoBmp.Height);

                canvas.Save();
                
                // 1. Aplicar la máscara de recorte Voronoi (coordenadas absolutas del lienzo)
                canvas.ClipRegion(clipRegion);
                
                // 2. Mover el origen al centro GPS de la foto
                canvas.Translate(cx, cy);
                
                // 3. Rotar el canvas según la dirección del vuelo
                canvas.RotateDegrees(rotation);

                using var imgShot = SKImage.FromBitmap(photoBmp);
                using var paint = new SKPaint();
                canvas.DrawImage(imgShot, srcRect, destRect, new SKSamplingOptions(SKFilterMode.Linear), paint);

                canvas.Restore();
            }
            catch { }

            if (si % 3 == 0 || si == totalShots - 1)
                progressCallback?.Invoke(pct, $"Mosaico Voronoi: foto {si + 1}/{totalShots} ({shot.PhotoFileName})...");
        }

        // --- Paso 4: Codificar y guardar ---
        progressCallback?.Invoke(90, "Codificando ortomosaico Voronoi v3...");

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var fileStream = File.OpenWrite(outputPath);
        data.SaveTo(fileStream);

        try
        {
            double pixelSizeX = lonSpan / targetWidth;
            double pixelSizeY = -latSpan / targetHeight;
            string worldFilePath = Path.ChangeExtension(outputPath, ".pgw");
            File.WriteAllText(worldFilePath, string.Format(CultureInfo.InvariantCulture,
                "{0:F10}\n0.0\n0.0\n{1:F10}\n{2:F10}\n{3:F10}\n",
                pixelSizeX, pixelSizeY, minLon, maxLat));
        }
        catch { }

        progressCallback?.Invoke(100, "¡Ortofoto Voronoi v3 generada exitosamente!");
        return outputPath;
    }

    /// <summary>
    /// Genera una vista previa en Base64 de una foto aérea de dron decodificada rápidamente
    /// </summary>
    public static string GetPhotoThumbnailBase64(string photoPath, int maxDimension = 900)
    {
        if (!File.Exists(photoPath)) return string.Empty;

        try
        {
            using var stream = File.OpenRead(photoPath);
            using var orig = SKBitmap.Decode(stream);
            if (orig == null) return string.Empty;

            int w = orig.Width;
            int h = orig.Height;
            if (w > maxDimension || h > maxDimension)
            {
                float ratio = Math.Min((float)maxDimension / w, (float)maxDimension / h);
                w = (int)(w * ratio);
                h = (int)(h * ratio);
            }

            var info = new SKImageInfo(w, h, SKColorType.Rgba8888);
            using var resized = new SKBitmap(info);
            orig.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear));
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

            return Convert.ToBase64String(data.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }
}
