using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerScan3D.App.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PowerScan3D.App.Core;

public class PdfReportService
{
    static PdfReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string GenerateReport(MissionModel mission, List<TreeModel> trees, double corridorWidthM, string outputPath, GeoTiffMetadata? tiffMeta = null)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Incluir ǭrboles en servidumbre, O cualquier ǭrbol que suponga un riesgo.
        var targetTrees = trees.Where(t => t.IsInsideCorridor || t.RiskLevel == "CRITICO" || t.RiskLevel == "ALTO" || t.RiskLevel == "MEDIO").ToList();
        var critTrees = targetTrees.Where(t => t.RiskLevel == "CRITICO").ToList();
        var highTrees = targetTrees.Where(t => t.RiskLevel == "ALTO").ToList();
        var medTrees = targetTrees.Where(t => t.RiskLevel == "MEDIO").ToList();
        var lowTrees = targetTrees.Where(t => t.RiskLevel == "BAJO").ToList();

        // Ordenar primero por riesgo, y luego por distancia al cable
        var sortedPriority = targetTrees
            .OrderBy(t => t.RiskLevel == "CRITICO" ? 0 : t.RiskLevel == "ALTO" ? 1 : t.RiskLevel == "MEDIO" ? 2 : 3)
            .ThenBy(t => t.DistanceToCableM)
            
            .ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(30);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                // 1. Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(titleCol =>
                        {
                            titleCol.Item().Text("AEROGRID | INFORME TÉCNICO DE VEGETACIÓN")
                                .Bold().FontSize(14).FontColor(Colors.Blue.Darken3);
                            titleCol.Item().Text("Inspección Aerofotogramétrica con Dron y Sobreposición KMZ")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });

                        row.ConstantItem(180).AlignRight().Column(metaCol =>
                        {
                            metaCol.Item().Text($"Fecha: {DateTime.Now:dd/MM/yyyy}").FontSize(8);
                            metaCol.Item().Text($"Servidumbre: {corridorWidthM} m (±{corridorWidthM / 2.0}m)").Bold().FontSize(8);
                            metaCol.Item().Text($"Doc: INF-CS-{mission.Id}").FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });

                    col.Item().PaddingTop(5).LineHorizontal(1.5f).LineColor(Colors.Blue.Medium);
                });

                // 2. Body
                page.Content().PaddingVertical(10).Column(col =>
                {
                    // Metadatos
                    col.Item().Text("1. DATOS GENERALES DEL LEVANTAMIENTO").Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(110);
                            columns.RelativeColumn();
                            columns.ConstantColumn(110);
                            columns.RelativeColumn();
                        });

                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Línea de Transmisión:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text(mission.Name);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Tensión Nominal:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text(mission.Voltage);

                        table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text("Sensor / Dron:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text(mission.DroneModel);
                        table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text("Altitud de Vuelo:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text(mission.AltitudeM);

                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Longitud Tramo:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text(mission.LengthKm);
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Ancho Servidumbre:").Bold();
                        table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text($"{corridorWidthM} metros");

                        // Métricas de Ortofoto (GSD, CRS, Área)
                        if (tiffMeta != null)
                        {
                            double gsdCm = tiffMeta.GsdMeters * 100.0;
                            double areaHa = Math.Abs((tiffMeta.MaxLon - tiffMeta.MinLon) * (tiffMeta.MaxLat - tiffMeta.MinLat)) * 12321.0; // aprox grados a hectáreas en latitudes medias
                            
                            table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text("Resolución (GSD):").Bold();
                            table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text($"{gsdCm:F1} cm/px");
                            table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text("Sistema Coordenadas:").Bold();
                            table.Cell().Background(Colors.Grey.Lighten5).Padding(4).Text($"{tiffMeta.CrsName} (EPSG:{tiffMeta.EpsgCode})");

                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Área Cubierta:").Bold();
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text($"~{areaHa:F1} hectáreas");
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text("Dimensiones Raster:").Bold();
                            table.Cell().Background(Colors.Grey.Lighten4).Padding(4).Text($"{tiffMeta.Width} x {tiffMeta.Height} px");
                        }
                    });

                    // KPIs
                    col.Item().PaddingTop(12).Text("2. RESUMEN EJECUTIVO DE VEGETACIÓN").Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Blue.Lighten2).Padding(6).Column(c =>
                        {
                            c.Item().Text($"{targetTrees.Count}").Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                            c.Item().Text("Total en Seguimiento").FontSize(7).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().Border(1).BorderColor(Colors.Red.Lighten2).Padding(6).Column(c =>
                        {
                            c.Item().Text($"{critTrees.Count}").Bold().FontSize(14).FontColor(Colors.Red.Darken2);
                            c.Item().Text("Críticos (< 4m)").FontSize(7).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().Border(1).BorderColor(Colors.Orange.Lighten2).Padding(6).Column(c =>
                        {
                            c.Item().Text($"{highTrees.Count}").Bold().FontSize(14).FontColor(Colors.Orange.Darken2);
                            c.Item().Text("Riesgo Alto (Cada/Cerca)").FontSize(7).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().Border(1).BorderColor(Colors.Green.Lighten2).Padding(6).Column(c =>
                        {
                            c.Item().Text($"{medTrees.Count}").Bold().FontSize(14).FontColor(Colors.Green.Darken2);
                            c.Item().Text("Preventivos (Medio)").FontSize(7).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(c =>
                        {
                            c.Item().Text($"{lowTrees.Count}").Bold().FontSize(14).FontColor(Colors.Grey.Darken1);
                            c.Item().Text("Bajo (Monitoreo)").FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // Tabla Detallada
                    col.Item().PaddingTop(12).Text($"3. INVENTARIO DE ÁRBOLES PRIORITARIOS (EN SERVIDUMBRE DE {corridorWidthM}M)").Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(75);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("ID").Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("Dist. Cable").Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("Riesgo").Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("Altura/Copa").Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("Especie").Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken4).Padding(3).Text("Acción Operativa").Bold().FontColor(Colors.White);
                        });

                        foreach (var t in sortedPriority)
                        {
                            var riskColor = t.RiskLevel == "CRITICO" ? Colors.Red.Darken2 :
                                            t.RiskLevel == "ALTO" ? Colors.Orange.Darken2 : Colors.Green.Darken2;

                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(t.Id).Bold();
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{t.DistanceToCableM} m");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(t.RiskLevel).Bold().FontColor(riskColor);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{t.HeightM}m / {t.CrownDiameterM}m");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(t.Species);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(t.RecommendedAction).FontSize(8);
                        }
                    });

                    // Firmas
                    col.Item().PaddingTop(20).Text("4. VALIDACIÓN TÉCNICA Y FIRMAS DE RESPONSABILIDAD").Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                    col.Item().PaddingTop(15).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            c.Item().AlignCenter().Text("Piloto UAV / Operador RPAS").Bold().FontSize(8);
                            c.Item().AlignCenter().Text("Levantamiento Aerofotogramétrico").FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(20);
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            c.Item().AlignCenter().Text("Ingeniero Geomático / SIG").Bold().FontSize(8);
                            c.Item().AlignCenter().Text("Procesamiento y Ortomosaico").FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(20);
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            c.Item().AlignCenter().Text("Jefe de Mantenimiento").Bold().FontSize(8);
                            c.Item().AlignCenter().Text("Aprobación Plan de Tala/Poda").FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });
                });

                // 3. Footer
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" de ");
                    x.TotalPages();
                });
            });
        });

        document.GeneratePdf(outputPath);
        return outputPath;
    }
}
