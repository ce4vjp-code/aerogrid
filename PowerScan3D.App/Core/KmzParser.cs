using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NetTopologySuite.Geometries;
using PowerScan3D.App.Models;

namespace PowerScan3D.App.Core;

public class KmzParseResult
{
    public string FileName { get; set; } = string.Empty;
    public string GeoJson { get; set; } = string.Empty;
    public List<Coordinate> LineCoordinates { get; set; } = new();
    public List<List<Coordinate>> AllLineSegments { get; set; } = new();
    public List<TowerModel> Towers { get; set; } = new();
    public int FeatureCount { get; set; }
}

public class KmzParser
{
    public static string ExtractKmlContent(byte[] fileBytes, string fileName)
    {
        if (fileName.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(fileBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var kmlEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
            if (kmlEntry == null)
                throw new FileNotFoundException("No se encontró ningún archivo .kml dentro del archivo .kmz");

            using var reader = new StreamReader(kmlEntry.Open(), System.Text.Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        else
        {
            using var ms = new MemoryStream(fileBytes);
            using var reader = new StreamReader(ms, System.Text.Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
    }

    public static KmzParseResult ParseKml(string kmlXml, string fileName)
    {
        var result = new KmzParseResult { FileName = fileName };

        string sanitizedXml = kmlXml;
        if (sanitizedXml.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            int closeIdx = sanitizedXml.IndexOf("?>", StringComparison.Ordinal);
            if (closeIdx > 0)
            {
                sanitizedXml = sanitizedXml.Substring(closeIdx + 2).Trim();
            }
        }

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(sanitizedXml);
        }
        catch
        {
            sanitizedXml = Regex.Replace(sanitizedXml, @"xmlns(:\w+)?=""[^""]*""", "");
            xdoc = XDocument.Parse(sanitizedXml);
        }

        var features = new List<object>();
        var placemarks = xdoc.Descendants().Where(e => e.Name.LocalName.Equals("Placemark", StringComparison.OrdinalIgnoreCase)).ToList();

        int elemIdx = 1;

        foreach (var pm in placemarks)
        {
            string name = pm.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            if (string.IsNullOrEmpty(name))
            {
                var simpleDatas = pm.Descendants().Where(e => e.Name.LocalName.Equals("SimpleData", StringComparison.OrdinalIgnoreCase)).ToList();
                var idTramo = simpleDatas.FirstOrDefault(s => (string?)s.Attribute("name") == "id_tramo")?.Value;
                var idPoste = simpleDatas.FirstOrDefault(s => (string?)s.Attribute("name") == "id_poste" || (string?)s.Attribute("name") == "posteinicio")?.Value;
                var etiqueta = simpleDatas.FirstOrDefault(s => (string?)s.Attribute("name") == "etiqueta")?.Value;

                if (!string.IsNullOrEmpty(idTramo)) name = $"Tramo {idTramo}";
                else if (!string.IsNullOrEmpty(idPoste)) name = $"Poste {idPoste}";
                else if (!string.IsNullOrEmpty(etiqueta)) name = etiqueta;
                else name = $"Elemento-{elemIdx}";
            }            string description = pm.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            // Extraer Z del poste si existe
            double pointZ = 12.0; // fallback de altura relativa
            var simpleDatasZ = pm.Descendants().Where(e => e.Name.LocalName.Equals("SimpleData", StringComparison.OrdinalIgnoreCase)).ToList();
            var altStr = simpleDatasZ.FirstOrDefault(s => (string?)s.Attribute("name") == "altura_sobre_nivel_mar")?.Value;
            if (double.TryParse(altStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedZ))
            {
                pointZ = parsedZ;
            }

            // 1. Puntos (Postes / Torres)
            var pointElems = pm.Descendants().Where(e => e.Name.LocalName.Equals("Point", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var pt in pointElems)
            {
                var coordsElem = pt.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("coordinates", StringComparison.OrdinalIgnoreCase));
                if (coordsElem != null)
                {
                    var coords = ParseCoordinatesBlock(coordsElem.Value);
                    if (coords.Count > 0)
                    {
                        var c = coords[0];
                        c.Z = pointZ; // Forzar la altitud real

                        features.Add(new
                        {
                            type = "Feature",
                            properties = new { name, description, geom_type = "Point" },
                            geometry = new { type = "Point", coordinates = new[] { c.X, c.Y, c.Z } }
                        });

                        result.Towers.Add(new TowerModel
                        {
                            Id = $"P-{elemIdx:D2}",
                            Name = name,
                            Latitude = c.Y,
                            Longitude = c.X,
                            HeightM = c.Z
                        });
                        elemIdx++;
                    }
                }
            }

            // 2. Líneas (Redes / Líneas de Transmisión)
            var lineElems = pm.Descendants().Where(e => e.Name.LocalName.Equals("LineString", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var ln in lineElems)
            {
                var coordsElem = ln.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("coordinates", StringComparison.OrdinalIgnoreCase));
                if (coordsElem != null)
                {
                    var coords = ParseCoordinatesBlock(coordsElem.Value);
                    if (coords.Count >= 2)
                    {
                        result.AllLineSegments.Add(coords);
                        result.LineCoordinates.AddRange(coords);

                        var lineCoordsList = coords.Select(c => new[] { c.X, c.Y, double.IsNaN(c.Z) ? 0 : c.Z }).ToList();
                        features.Add(new
                        {
                            type = "Feature",
                            properties = new { name, description, geom_type = "LineString" },
                            geometry = new { type = "LineString", coordinates = lineCoordsList }
                        });
                        elemIdx++;
                    }
                }
            }
        }

        result.FeatureCount = features.Count;

        var featureCollection = new
        {
            type = "FeatureCollection",
            features
        };

        result.GeoJson = JsonSerializer.Serialize(featureCollection, new JsonSerializerOptions { WriteIndented = false });
        return result;
    }

    /// <summary>
    /// Une múltiples resultados de archivos KML/KMZ cargados simultáneamente (Redes + Postes)
    /// </summary>
    public static KmzParseResult MergeResults(List<KmzParseResult> results)
    {
        if (results.Count == 1) return results[0];

        var merged = new KmzParseResult
        {
            FileName = string.Join(" + ", results.Select(r => r.FileName))
        };

        var allFeatures = new List<object>();

        foreach (var r in results)
        {
            merged.LineCoordinates.AddRange(r.LineCoordinates);
            merged.AllLineSegments.AddRange(r.AllLineSegments);
            merged.Towers.AddRange(r.Towers);

            if (!string.IsNullOrEmpty(r.GeoJson))
            {
                using var doc = JsonDocument.Parse(r.GeoJson);
                if (doc.RootElement.TryGetProperty("features", out var feats) && feats.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in feats.EnumerateArray())
                    {
                        allFeatures.Add(JsonSerializer.Deserialize<object>(f.GetRawText())!);
                    }
                }
            }
        }

        // FASE 3: Reconstrucción Altitudinal (Cruzando Postes con Cables)
        // Buscamos inyectar la coordenada Z a las líneas que son 2D.
        if (merged.Towers.Count > 0 && merged.AllLineSegments.Count > 0)
        {
            double toleranceDegrees = 0.0001; // ~11 metros
            
            foreach (var segment in merged.AllLineSegments)
            {
                for (int i = 0; i < segment.Count; i++)
                {
                    var coord = segment[i];
                    if (double.IsNaN(coord.Z) || coord.Z == 0) // Si no tiene Z o es 0
                    {
                        // Buscar el poste más cercano
                        var closestTower = merged.Towers
                            .OrderBy(t => Math.Pow(t.Longitude - coord.X, 2) + Math.Pow(t.Latitude - coord.Y, 2))
                            .FirstOrDefault();

                        if (closestTower != null)
                        {
                            double dist = Math.Sqrt(Math.Pow(closestTower.Longitude - coord.X, 2) + Math.Pow(closestTower.Latitude - coord.Y, 2));
                            if (dist < toleranceDegrees)
                            {
                                coord.Z = closestTower.HeightM;
                            }
                        }
                    }
                }
            }
        }

        merged.FeatureCount = allFeatures.Count;
        var featureCollection = new
        {
            type = "FeatureCollection",
            features = allFeatures
        };

        merged.GeoJson = JsonSerializer.Serialize(featureCollection, new JsonSerializerOptions { WriteIndented = false });
        return merged;
    }

    private static List<Coordinate> ParseCoordinatesBlock(string coordsText)
    {
        var list = new List<Coordinate>();
        if (string.IsNullOrWhiteSpace(coordsText)) return list;

        var rawTokens = coordsText.Trim().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in rawTokens)
        {
            var parts = token.Split(',');
            if (parts.Length >= 2)
            {
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    double z = double.NaN;
                    if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedZ))
                    {
                        z = parsedZ;
                    }

                    if (Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
                    {
                        var c = new Coordinate(lon, lat);
                        if (!double.IsNaN(z)) c.Z = z;
                        list.Add(c);
                    }
                }
            }
        }
        return list;
    }

    public static void CreateSampleKmz(string outputPath)
    {
        string dir = Path.GetDirectoryName(outputPath) ?? "";
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string sampleKml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2"">
  <Document>
    <name>LAT_220kV_Muestra.kml</name>
    <Placemark>
      <name>Tramo Principal LAT 220kV</name>
      <LineString>
        <coordinates>
          -70.6482,-33.4372,0 -70.6450,-33.4350,0 -70.6420,-33.4330,0 -70.6390,-33.4310,0
        </coordinates>
      </LineString>
    </Placemark>
  </Document>
</kml>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("doc.kml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(sampleKml);
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
    }
}
