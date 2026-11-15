using Web.Monitoring;

namespace Web.Models;

public sealed record HealthMetric(string Name, string Value, string Unit, string State, string Note, DateTimeOffset? At);
public sealed class CrewDashboardModel
{
    public int Days { get; init; } = 7;
    public static double? DewPoint(double temperature, double humidity)
    {
        if (!double.IsFinite(temperature) || !double.IsFinite(humidity) || temperature is < -40 or > 60 || humidity is <= 0 or > 100) return null;
        var gamma = Math.Log(humidity / 100) + 17.625 * temperature / (243.04 + temperature);
        return 243.04 * gamma / (17.625 - gamma);
    }
    public string Name { get; init; } = "Crew member";
    public string? Pin { get; init; }
    public string? Error { get; init; }
    public MonitoringRecord Record { get; init; } = new([], []);
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    public MeasurementSample? Latest => Record.Measurements.OrderByDescending(x => x.MeasuredAt).FirstOrDefault();
    public EnvironmentSample? Cabin => Record.Environment.OrderByDescending(x => x.MeasuredAt).FirstOrDefault();
    public MeasurementSample? Photo => Record.Measurements.Where(x => x.PhotoUrl is not null).OrderByDescending(x => x.MeasuredAt).FirstOrDefault();
    public DateTimeOffset? NextCheck => Latest?.MeasuredAt.AddHours(6);
    public bool Due => NextCheck is null || NextCheck <= Now;
    public string CabinState => Cabin is null ? "unknown" : Cabin.TemperatureC is < 18 or > 27 || Cabin.RelativeHumidityPercent is < 25 or > 75 ? "review" : "good";
    public static string PulseState(MeasurementSample? sample) => sample is null || sample.Pulse.Bpm <= 0 ? "unknown" : sample.Pulse.Quality != "good" ? "unknown" : sample.Pulse.Bpm is < 60 or > 100 ? "review" : "good";
    public string State => Error is not null || Latest is null ? "unknown" : PulseState(Latest) == "review" || CabinState == "review" ? "review" : Due || PulseState(Latest) == "unknown" || Cabin is null || Now - Cabin.MeasuredAt > TimeSpan.FromHours(6) ? "unknown" : "good";
    public string Status => State switch { "good" => "Measured indicators in range", "review" => "Review recommended", _ => "Assessment incomplete" };
    public IReadOnlyList<HealthMetric> Metrics => [
        new("Heart rate", Latest?.Pulse.Bpm > 0 ? Latest.Pulse.Bpm.ToString() : "Not measured", "bpm", PulseState(Latest), Latest is null ? "Awaiting hardware reading" : $"Signal: {Latest.Pulse.Quality}. {Latest.Pulse.StatusReason}", Latest?.MeasuredAt),
        new("Cabin temperature", Cabin?.TemperatureC.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "Not measured", "°C", CabinState, "Cabin screening: 18–27 °C; humidity also matters", Cabin?.MeasuredAt),
        new("Cabin humidity", Cabin?.RelativeHumidityPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "Not measured", "%", CabinState, "Cabin screening: 25–75%; temperature also matters", Cabin?.MeasuredAt)
    ];
}

