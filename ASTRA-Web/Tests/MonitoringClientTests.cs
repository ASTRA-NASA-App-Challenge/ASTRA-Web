using System.Net;
using System.Text;
using Web.Monitoring;

namespace Tests;

public class MonitoringClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task RegistrationPreservesLeadingZeroesAndSupportsRetries(HttpStatusCode status)
    {
        var handler = new Stub(async request =>
        {
            Assert.Equal("/api/v1/crew", request.RequestUri!.AbsolutePath);
            Assert.Equal("{\"pin\":\"012345\"}", await request.Content!.ReadAsStringAsync());
            return new(status);
        });
        await Client(handler).RegisterAsync("012345", default);
    }

    [Fact]
    public async Task RegistrationFailureRemainsRetryable()
    {
        var client = Client(new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RegisterAsync("012345", default));
    }

    [Fact]
    public async Task ReadsSnakeCaseDataByPin()
    {
        var client = Client(new Stub(request =>
        {
            Assert.StartsWith("/api/v1/crew/012345/", request.RequestUri!.AbsolutePath);
            var json = request.RequestUri.AbsolutePath.EndsWith("measurements")
                ? """{"items":[{"measurement_id":"mea_test","measured_at":"2026-11-15T10:00:00Z","pulse":{"bpm":72,"quality":"good"},"photo_url":"ignored"}],"next_cursor":null}"""
                : """{"items":[{"measured_at":"2026-11-15T10:00:00Z","temperature_c":22.5,"relative_humidity_percent":45}],"next_cursor":null}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));
        var record = await client.ReadAsync("012345", default);
        Assert.Equal("mea_test", Assert.Single(record.Measurements).MeasurementId);
        Assert.Equal(72, record.Measurements[0].Pulse.Bpm);
        Assert.Equal(22.5, Assert.Single(record.Environment).TemperatureC);
    }

    [Fact]
    public async Task PhotosAreRequestedUnderTheMembersPin()
    {
        var client = Client(new Stub(request =>
        {
            Assert.Equal("/api/v1/crew/012345/measurements/mea_test/photo", request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(response);
        }));
        Assert.Equal(new byte[] { 1, 2, 3 }, await client.PhotoAsync("012345", "mea_test", default));
    }

    [Fact]
    public async Task InvalidPinDoesNotSendARequest()
    {
        var client = Client(new Stub(_ => throw new Exception("Should not send")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RegisterAsync("../123", default));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    public async Task HistoryLoadsAllPagesWithStableTimeBounds(int days)
    {
        var queries = new List<string>();
        var client = Client(new Stub(request =>
        {
            queries.Add(request.RequestUri!.Query);
            var secondPage = request.RequestUri.Query.Contains("cursor=");
            var measurements = request.RequestUri.AbsolutePath.EndsWith("measurements");
            var item = measurements
                ? """{"measurement_id":"sample","measured_at":"2026-11-15T10:00:00Z","pulse":{"bpm":72,"quality":"good"},"photo_url":null}"""
                : """{"measured_at":"2026-11-15T10:00:00Z","temperature_c":22,"relative_humidity_percent":45}""";
            var json = "{\"items\":[" + item + "],\"next_cursor\":" + (secondPage ? "null" : "\"cursor+/=\"") + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));
        var record = await client.ReadWindowAsync("012345", default, days);
        Assert.Equal(2, record.Measurements.Count);
        Assert.Equal(2, record.Environment.Count);
        Assert.Equal(4, queries.Count);
        Assert.Equal(queries[0], queries[2]);
        Assert.StartsWith(queries[0] + "&cursor=", queries[1]);
        var bounds = queries[0].TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        Assert.Equal(TimeSpan.FromDays(days), DateTimeOffset.Parse(bounds["to"]) - DateTimeOffset.Parse(bounds["from"]));
    }

    [Fact]
    public async Task DemoStatusHandlesOlderServers()
    {
        var client = Client(new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await client.ReadDemoStatusAsync(default));
    }

    [Fact]
    public async Task DemoStatusReadsAuthenticatedServerCounts()
    {
        var client = Client(new Stub(request =>
        {
            Assert.Equal("/api/v1/demo/status", request.RequestUri!.AbsolutePath);
            var json = """{"state":"verifying","registered_pins":6,"expected_pins":6,"measurements":750,"environment_readings":750,"uploaded_photos":18,"expected_measurements":750,"expected_environment_readings":750,"expected_photos":18}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }));
        var status = await client.ReadDemoStatusAsync(default);
        Assert.NotNull(status);
        Assert.Equal("verifying", status.State);
        Assert.Equal(750, status.Measurements);
        Assert.Equal(750, status.EnvironmentReadings);
        Assert.Equal(18, status.UploadedPhotos);
        Assert.Equal(6, status.RegisteredPins);
    }
    private static MonitoringClient Client(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("http://localhost/") });
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
