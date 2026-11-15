using System.Net;
using System.Text;
using Web.Monitoring;

namespace Tests;

public class MonitoringClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Conflict)]
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
                : """[{"measured_at":"2026-11-15T10:00:00Z","temperature_c":22.5,"relative_humidity_percent":45}]""";
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

    private static MonitoringClient Client(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("http://localhost/") });
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
