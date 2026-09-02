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

        double minDistance2D = double.MaxValue;
        double minDistance3D = double.MaxValue;
        double minClearanceV = double.MaxValue;
        bool has3DData = false;

        foreach (var segment in lineSegments)
        {
            if (segment.Count < 2) continue;

            double refLat = segment[0].Y;
            double metersPerDegreeLon = 111320.0 * Math.Cos(refLat * Math.PI / 180.0);

            var projectedCoords = segment.Select(c => {
                double zVal = double.IsNaN(c.Z) ? 0 : c.Z;
                return new CoordinateZ(
                    c.X * metersPerDegreeLon,
                    c.Y * _metersPerDegreeLat,
                    zVal
                );
            }).ToArray();

            var lineString = _factory.CreateLineString(projectedCoords);
            var treePoint = _factory.CreatePoint(new Coordinate(
                tree.Longitude * metersPerDegreeLon,
                tree.Latitude * _metersPerDegreeLat
            ));

            double d = lineString.Distance(treePoint);
            
            if (d < minDistance2D) 
            {
                minDistance2D = d;
                
                // Extraer el punto más cercano en la línea
                var distOp = new NetTopologySuite.Operation.Distance.DistanceOp(lineString, treePoint);
                var closestPts = distOp.NearestPoints();
                var ptOnLine = closestPts[0];

                // Interpolar Z en el segmento
                double cableZ = 0;
                for (int i = 0; i < projectedCoords.Length - 1; i++)
                {
                    var p1 = projectedCoords[i];
                    var p2 = projectedCoords[i + 1];
                    
                    double segLen = p1.Distance(p2);
                    double d1 = p1.Distance(ptOnLine);
                    double d2 = p2.Distance(ptOnLine);
                    
                    if (Math.Abs((d1 + d2) - segLen) < 0.01) // El punto está en este segmento
                    {
                        double fraction = segLen > 0 ? d1 / segLen : 0;
                        cableZ = p1.Z + fraction * (p2.Z - p1.Z);
                        break;
                    }
                }

                if (cableZ > 0 && tree.AbsoluteElevationM > 0)
                {
                    has3DData = true;
                    double treeTopZ = tree.AbsoluteElevationM;
                    double treeBaseZ = treeTopZ - tree.HeightM;
                    
                    minClearanceV = cableZ - treeTopZ;
                    
                    // Distancia 3D desde la BASE del árbol hasta el cable
                    double dVertical = cableZ - treeBaseZ;
                    minDistance3D = Math.Sqrt((d * d) + (dVertical * dVertical));
                }
            }
        }

        if (minDistance2D == double.MaxValue) return tree;

        tree.DistanceToCableM = Math.Round(minDistance2D, 2);
        tree.IsInsideCorridor = minDistance2D <= halfWidth;

        if (has3DData)
        {
            // MOTOR 3D: Análisis Geométrico
            double radioCaida = tree.HeightM + (tree.CrownDiameterM / 2.0);

            if (minDistance2D <= halfWidth && minClearanceV < 4.0)
            {
                tree.RiskLevel = "CRITICO";
                tree.RiskColor = "#ff3366";
                tree.RecommendedAction = "Tala Inmediata (Riesgo Invasivo - Contacto inminente)";
            }
            else if (minDistance3D < radioCaida)
            {
                tree.RiskLevel = "ALTO";
                tree.RiskColor = "#ff9900";
                tree.RecommendedAction = "Poda / Tala (Riesgo de Caída Cilíndrica sobre red)";
            }
            else if (tree.IsInsideCorridor)
            {
                tree.RiskLevel = "MEDIO";
                tree.RiskColor = "#ffcc00";
                tree.RecommendedAction = "Monitoreo (Dentro de faja, sin riesgo de caída)";
            }
            else
            {
                tree.RiskLevel = "BAJO";
                tree.RiskColor = "#00e676";
                tree.RecommendedAction = "Seguro";
            }
        }
        else
        {
            // Fallback 2D clásico + Caída Cilíndrica
            double radioCaida = tree.HeightM + (tree.CrownDiameterM / 2.0);
            
            if (tree.IsInsideCorridor)
            {
                if (tree.HeightM > 3.0)
                {
                    tree.RiskLevel = "CRITICO";
                    tree.RiskColor = "#ff3366";
                    tree.RecommendedAction = "Poda / Tala Inmediata (Peligro en servidumbre 2D)";
                }
                else
                {
                    tree.RiskLevel = "MEDIO";
                    tree.RiskColor = "#ffcc00";
                    tree.RecommendedAction = "Notificar propietario (Matorral/Cerco vivo)";
                }
            }
            else if (minDistance2D < radioCaida)
            {
                tree.RiskLevel = "ALTO";
                tree.RiskColor = "#ff9900";
                tree.RecommendedAction = "Poda / Tala (Riesgo de Caída sobre red 2D)";
            }
            else
            {
                tree.RiskLevel = "BAJO";
                tree.RiskColor = "#00e676";
                tree.RecommendedAction = "Fuera de servidumbre y sin riesgo de caída";
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
