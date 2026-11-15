namespace Common.Models;

// Prototype visual screening bands, not validated clinical triage thresholds.
// Resting pulse context: AHA; broad cabin reference bounds: NASA-STD-3001 Vol 2.
public static class HealthSeverity
{
    public static int Rank(string state) => state switch { "good" => 0, "watch" => 1, "caution" => 2, "high" => 3, "critical" => 4, _ => -1 };
    public static string Worst(params string[] states) => states.OrderByDescending(Rank).FirstOrDefault() ?? "unknown";
    public static string Label(string state) => state switch
    {
        "good" => "In range", "watch" => "Slight deviation", "caution" => "Caution",
        "high" => "High alert", "critical" => "Urgent review", _ => "Repeat reading"
    };
    public static string Pulse(int bpm, string quality) => bpm <= 0 || quality != "good" ? "unknown"
        : bpm is < 35 or > 160 ? "critical"
        : bpm is < 40 or > 130 ? "high"
        : bpm is < 50 or > 110 ? "caution"
        : bpm is < 60 or > 100 ? "watch" : "good";
    public static string Temperature(double value) => !double.IsFinite(value) ? "unknown"
        : value is < 10 or > 35 ? "critical"
        : value is < 14 or > 32 ? "high"
        : value is < 16 or > 29 ? "caution"
        : value is < 18 or > 27 ? "watch" : "good";
    public static string Humidity(double value) => !double.IsFinite(value) || value is < 0 or > 100 ? "unknown"
        : value is < 10 or > 90 ? "critical"
        : value is < 15 or > 85 ? "high"
        : value is < 20 or > 80 ? "caution"
        : value is < 25 or > 75 ? "watch" : "good";
}
