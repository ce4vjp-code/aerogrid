namespace PowerScan3D.App.Models;

public class TreeModel
{
    public string Id { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double HeightM { get; set; }
    public double CrownDiameterM { get; set; }
    public string Species { get; set; } = string.Empty;
    public double ConfidencePct { get; set; }
    public string SourcePhoto { get; set; } = string.Empty;

    // Calculados por GisEngine
    public double DistanceToCableM { get; set; }
    public bool IsInsideCorridor { get; set; }
    public string RiskLevel { get; set; } = "BAJO"; // CRITICO, ALTO, MEDIO, BAJO
    public string RiskColor { get; set; } = "#00e676";
    public string RecommendedAction { get; set; } = string.Empty;
}

public class TowerModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double HeightM { get; set; }
    public string Type { get; set; } = "Metálica de Celosía 220kV";
}

public class MissionModel
{
    public string Id { get; set; } = "MISSION-2026-01";
    public string Name { get; set; } = "LAT 220kV Alto Jahuel - Polpaico (Tramo Quebrada)";
    public string Voltage { get; set; } = "220 kV";
    public string LengthKm { get; set; } = "1.8 km";
    public string DroneModel { get; set; } = "DJI Matrice 350 RTK + Zenmuse P1";
    public string AltitudeM { get; set; } = "65 m AGL";
    public string Date { get; set; } = "2026-08-20";
    public List<TowerModel> Towers { get; set; } = new();
}
