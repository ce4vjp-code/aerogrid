using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using PowerScan3D.App.Models;

namespace PowerScan3D.App.Core;

public class GisEngine
{
    private readonly GeometryFactory _factory = new();
    private readonly double _metersPerDegreeLat = 111320.0;

    public double CorridorWidthM { get; set; } = 20.0;

    public GisEngine(double corridorWidthM = 20.0)
    {
        CorridorWidthM = corridorWidthM;
    }

    /// <summary>
    /// Calcula la distancia mínima ortogonal de un árbol al cable sobre múltiples tramos y determina su riesgo.
    /// </summary>
    public TreeModel AnalyzeTreeMultiSegment(TreeModel tree, List<List<Coordinate>> lineSegments, double? corridorWidthM = null)
    {
        double width = corridorWidthM ?? CorridorWidthM;
        double halfWidth = width / 2.0;

        if (lineSegments == null || !lineSegments.Any())
            return tree;

        double minDistanceM = double.MaxValue;

        foreach (var segment in lineSegments)
        {
            if (segment.Count < 2) continue;

            double refLat = segment[0].Y;
            double metersPerDegreeLon = 111320.0 * Math.Cos(refLat * Math.PI / 180.0);

            var projectedCoords = segment.Select(c => new Coordinate(
                c.X * metersPerDegreeLon,
                c.Y * _metersPerDegreeLat
            )).ToArray();

            var lineString = _factory.CreateLineString(projectedCoords);
            var treePoint = _factory.CreatePoint(new Coordinate(
                tree.Longitude * metersPerDegreeLon,
                tree.Latitude * _metersPerDegreeLat
            ));

            double d = lineString.Distance(treePoint);
            if (d < minDistanceM) minDistanceM = d;
        }

        if (minDistanceM == double.MaxValue) return tree;

        tree.DistanceToCableM = Math.Round(minDistanceM, 2);
        tree.IsInsideCorridor = minDistanceM <= halfWidth;

        if (tree.IsInsideCorridor)
        {
            if (tree.HeightM > 3.0)
            {
                tree.RiskLevel = "CRITICO";
                tree.RiskColor = "#ff3366";
                tree.RecommendedAction = "Poda / Tala Inmediata (Peligro en servidumbre)";
            }
            else
            {
                tree.RiskLevel = "MEDIO";
                tree.RiskColor = "#ffcc00";
                tree.RecommendedAction = "Notificar propietario (Matorral/Cerco vivo)";
            }
        }
        else
        {
            if (tree.HeightM > minDistanceM)
            {
                tree.RiskLevel = "ALTO";
                tree.RiskColor = "#ff9900";
                tree.RecommendedAction = "Notificación a propietario (Riesgo de caída)";
            }
            else
            {
                tree.RiskLevel = "BAJO";
                tree.RiskColor = "#78909c";
                tree.RecommendedAction = "Fuera de peligro (Sin acción requerida)";
            }
        }

        return tree;
    }

    public TreeModel AnalyzeTree(TreeModel tree, List<Coordinate> lineCoords, double? corridorWidthM = null)
    {
        return AnalyzeTreeMultiSegment(tree, new List<List<Coordinate>> { lineCoords }, corridorWidthM);
    }

    /// <summary>
    /// Genera los polígonos de servidumbre individuales para cada tramo de la red eléctrica sin unirlos erróneamente
    /// </summary>
    public List<List<double[]>> GenerateMultiCorridorPolygons(List<List<Coordinate>> lineSegments, double? corridorWidthM = null)
    {
        var result = new List<List<double[]>>();
        if (lineSegments == null) return result;

        double width = corridorWidthM ?? CorridorWidthM;

        foreach (var seg in lineSegments)
        {
            if (seg.Count >= 2)
            {
                var poly = GenerateSingleSegmentCorridor(seg, width);
                if (poly.Count >= 3)
                {
                    result.Add(poly);
                }
            }
        }

        return result;
    }

    public List<double[]> GenerateCorridorPolygon(List<Coordinate> lineCoords, double? corridorWidthM = null)
    {
        if (lineCoords == null || lineCoords.Count < 2) return new List<double[]>();
        return GenerateSingleSegmentCorridor(lineCoords, corridorWidthM ?? CorridorWidthM);
    }

    private List<double[]> GenerateSingleSegmentCorridor(List<Coordinate> segment, double width)
    {
        double halfWidth = width / 2.0;
        var polygon = new List<double[]>();
        if (segment == null || segment.Count < 2) return polygon;

        double refLat = segment[0].Y;
        double metersPerDegreeLon = 111320.0 * Math.Cos(refLat * Math.PI / 180.0);

        var leftSide = new List<double[]>();
        var rightSide = new List<double[]>();

        for (int i = 0; i < segment.Count - 1; i++)
        {
            var p1 = segment[i];
            var p2 = segment[i + 1];

            double dLat = (p2.Y - p1.Y) * _metersPerDegreeLat;
            double dLon = (p2.X - p1.X) * metersPerDegreeLon;
            double len = Math.Sqrt(dLat * dLat + dLon * dLon);
            if (len < 0.001) continue;

            double nLat = -dLon / len;
            double nLon = dLat / len;

            leftSide.Add(new double[] {
                p1.X + (nLon * halfWidth / metersPerDegreeLon),
                p1.Y + (nLat * halfWidth / _metersPerDegreeLat)
            });

            rightSide.Insert(0, new double[] {
                p1.X - (nLon * halfWidth / metersPerDegreeLon),
                p1.Y - (nLat * halfWidth / _metersPerDegreeLat)
            });

            if (i == segment.Count - 2)
            {
                leftSide.Add(new double[] {
                    p2.X + (nLon * halfWidth / metersPerDegreeLon),
                    p2.Y + (nLat * halfWidth / _metersPerDegreeLat)
                });

                rightSide.Insert(0, new double[] {
                    p2.X - (nLon * halfWidth / metersPerDegreeLon),
                    p2.Y - (nLat * halfWidth / _metersPerDegreeLat)
                });
            }
        }

        polygon.AddRange(leftSide);
        polygon.AddRange(rightSide);
        return polygon;
    }
}
