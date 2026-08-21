using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerScan3D.App.Core;

public class LibraryItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "kmz", "geotiff", "telemetry"
    public string SizeFormatted { get; set; } = string.Empty;
    public string ModifiedDate { get; set; } = string.Empty;
}

public class LibraryCatalog
{
    public List<LibraryItem> KmzLayers { get; set; } = new();
    public List<LibraryItem> GeoTiffs { get; set; } = new();
    public List<LibraryItem> TelemetryFiles { get; set; } = new();
    public string LibraryDirectory { get; set; } = string.Empty;
}

public class LibraryService
{
    public static string GetLibraryRoot()
    {
        string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string libPath = Path.Combine(downloadsDir, "PowerScan3D-CSharp", "biblioteca_capas");
        Directory.CreateDirectory(libPath);
        Directory.CreateDirectory(Path.Combine(libPath, "kmz_kml"));
        Directory.CreateDirectory(Path.Combine(libPath, "ortofotos_geotiff"));
        Directory.CreateDirectory(Path.Combine(libPath, "telemetria_dron"));
        return libPath;
    }

    public static string SaveToLibrary(string sourceFilePath, string category)
    {
        if (!File.Exists(sourceFilePath)) return sourceFilePath;

        string root = GetLibraryRoot();
        string subDir = category switch
        {
            "geotiff" => Path.Combine(root, "ortofotos_geotiff"),
            "telemetry" => Path.Combine(root, "telemetria_dron"),
            _ => Path.Combine(root, "kmz_kml")
        };

        string fileName = Path.GetFileName(sourceFilePath);
        string destPath = Path.Combine(subDir, fileName);

        // Copiar si no es el mismo archivo
        if (!string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFilePath, destPath, true);
        }

        return destPath;
    }

    public static LibraryCatalog GetCatalog()
    {
        string root = GetLibraryRoot();
        var catalog = new LibraryCatalog { LibraryDirectory = root };

        // 1. KMZ / KML
        string kmzDir = Path.Combine(root, "kmz_kml");
        if (Directory.Exists(kmzDir))
        {
            var files = Directory.GetFiles(kmzDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));

            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                catalog.KmzLayers.Add(new LibraryItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    Category = "kmz",
                    SizeFormatted = FormatFileSize(fi.Length),
                    ModifiedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        // 2. GeoTIFF
        string tiffDir = Path.Combine(root, "ortofotos_geotiff");
        if (Directory.Exists(tiffDir))
        {
            var files = Directory.GetFiles(tiffDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                catalog.GeoTiffs.Add(new LibraryItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    Category = "geotiff",
                    SizeFormatted = FormatFileSize(fi.Length),
                    ModifiedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        // 3. Telemetría
        string telemDir = Path.Combine(root, "telemetria_dron");
        if (Directory.Exists(telemDir))
        {
            var files = Directory.GetFiles(telemDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".mrk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                catalog.TelemetryFiles.Add(new LibraryItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    Category = "telemetry",
                    SizeFormatted = FormatFileSize(fi.Length),
                    ModifiedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }

        return catalog;
    }

    public static void OpenFolderInExplorer()
    {
        string root = GetLibraryRoot();
        if (Directory.Exists(root))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
