using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Web.Monitoring;

public sealed class MonitoringClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task RegisterAsync(string pin, CancellationToken ct)
    {
        ValidatePin(pin);
        using var response = await http.PostAsJsonAsync("api/v1/crew", new { pin }, Json, ct);
        if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
    }

    public async Task<MonitoringRecord> ReadAsync(string pin, CancellationToken ct)
    {
        ValidatePin(pin);
        var prefix = $"api/v1/crew/{pin}/";
        var measurements = await http.GetFromJsonAsync<MeasurementPage>(prefix + "measurements?limit=100", Json, ct);
        var readings = await http.GetFromJsonAsync<List<EnvironmentSample>>(prefix + "environment-readings?limit=100", Json, ct);
        return new(measurements?.Items ?? [], readings ?? []);
    }

    public async Task<byte[]?> PhotoAsync(string pin, string measurementId, CancellationToken ct)
    {
        ValidatePin(pin);
        using var response = await http.GetAsync($"api/v1/crew/{pin}/measurements/{Uri.EscapeDataString(measurementId)}/photo", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "image/jpeg") throw new HttpRequestException("Unexpected photo format.");
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static void ValidatePin(string pin)
    {
        if (pin.Length != 6 || pin.Any(c => c is < '0' or > '9')) throw new ArgumentException("Invalid member PIN.");
    }
}

public sealed record MeasurementPage(List<MeasurementSample> Items, string? NextCursor);
public sealed record MeasurementSample(string MeasurementId, DateTimeOffset MeasuredAt, PulseSample Pulse, string? PhotoUrl);
public sealed record PulseSample(int Bpm, string Quality, string? StatusReason);
public sealed record EnvironmentSample(DateTimeOffset MeasuredAt, double TemperatureC, double RelativeHumidityPercent);
public sealed record MonitoringRecord(List<MeasurementSample> Measurements, List<EnvironmentSample> Environment);
public sealed record MonitoringPage(string MemberId, string Name, MonitoringRecord? Record, string? Error);
