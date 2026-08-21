using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using PowerScan3D.App.Models;

namespace PowerScan3D.App.Core;

public class MockDataService
{
    public static MissionModel GetDefaultMission()
    {
        return new MissionModel
        {
            Id = "MISSION-2026-01",
            Name = "LAT 220kV Alto Jahuel - Polpaico (Tramo Quebrada)",
            Voltage = "220 kV",
            LengthKm = "1.8 km",
            DroneModel = "DJI Matrice 350 RTK + Zenmuse P1",
            AltitudeM = "65 m AGL",
            Date = "2026-08-20",
            Towers = new List<TowerModel>
            {
                new() { Id = "T-01", Name = "Torre T-01 (Anclaje)", Latitude = -33.4372, Longitude = -70.6482, HeightM = 35.0 },
                new() { Id = "T-02", Name = "Torre T-02 (Suspensión)", Latitude = -33.4347, Longitude = -70.6462, HeightM = 32.5 },
                new() { Id = "T-03", Name = "Torre T-03 (Suspensión)", Latitude = -33.4322, Longitude = -70.6441, HeightM = 32.5 },
                new() { Id = "T-04", Name = "Torre T-04 (Suspensión)", Latitude = -33.4297, Longitude = -70.6421, HeightM = 32.5 },
                new() { Id = "T-05", Name = "Torre T-05 (Suspensión)", Latitude = -33.4272, Longitude = -70.6401, HeightM = 32.5 },
                new() { Id = "T-06", Name = "Torre T-06 (Anclaje)", Latitude = -33.4247, Longitude = -70.6380, HeightM = 36.0 }
            }
        };
    }

    public static List<Coordinate> GetMissionCoordinates(MissionModel mission)
    {
        var list = new List<Coordinate>();
        foreach (var t in mission.Towers)
        {
            list.Add(new Coordinate(t.Longitude, t.Latitude));
        }
        return list;
    }

    public static List<TreeModel> GenerateTrees(MissionModel mission, double corridorWidthM = 20.0)
    {
        var trees = new List<TreeModel>();
        var speciesList = new[]
        {
            "Eucalipto (Eucalyptus globulus)",
            "Pino Radiata (Pinus radiata)",
            "Álamo (Populus nigra)",
            "Acacia (Acacia dealbata)",
            "Espino (Vachellia caven)"
        };

        var towers = mission.Towers;
        int treeId = 1;
        double metersPerDegreeLat = 111320.0;
        double metersPerDegreeLon = 111320.0 * Math.Cos(towers[0].Latitude * Math.PI / 180.0);

        var gisEngine = new GisEngine(corridorWidthM);
        var lineCoords = GetMissionCoordinates(mission);

        for (int seg = 0; seg < towers.Count - 1; seg++)
        {
            var p1 = towers[seg];
            var p2 = towers[seg + 1];

            double dLat = (p2.Latitude - p1.Latitude) * metersPerDegreeLat;
            double dLon = (p2.Longitude - p1.Longitude) * metersPerDegreeLon;
            double len = Math.Sqrt(dLat * dLat + dLon * dLon);

            double nLat = -dLon / len;
            double nLon = dLat / len;

            int numTreesInSpan = 11;
            for (int j = 0; j < numTreesInSpan; j++)
            {
                double frac = 0.08 + (j / (double)(numTreesInSpan - 1)) * 0.84;
                double seedOffset = Math.Sin(treeId * 3.7) * 14.5;

                double centerLat = p1.Latitude + (p2.Latitude - p1.Latitude) * frac;
                double centerLon = p1.Longitude + (p2.Longitude - p1.Longitude) * frac;

                double treeLat = centerLat + (nLat * seedOffset / metersPerDegreeLat);
                double treeLon = centerLon + (nLon * seedOffset / metersPerDegreeLon);

                double heightM = Math.Round(7.0 + Math.Abs(Math.Sin(treeId * 1.5)) * 17.0, 1);
                double crownM = Math.Round(3.5 + Math.Abs(Math.Cos(treeId * 2.1)) * 5.5, 1);
                double conf = Math.Round(89.0 + Math.Abs(Math.Sin(treeId * 4.3)) * 10.5, 1);
                string species = speciesList[treeId % speciesList.Length];

                var tree = new TreeModel
                {
                    Id = $"ARB-{treeId:D3}",
                    Latitude = treeLat,
                    Longitude = treeLon,
                    HeightM = heightM,
                    CrownDiameterM = crownM,
                    Species = species,
                    ConfidencePct = conf,
                    SourcePhoto = $"DJI_20260820_{100 + treeId:D3}.JPG"
                };

                gisEngine.AnalyzeTree(tree, lineCoords, corridorWidthM);
                trees.Add(tree);
                treeId++;
            }
        }

        return trees;
    }
}
