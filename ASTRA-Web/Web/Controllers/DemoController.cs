using Common.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Monitoring;

namespace Web.Controllers;

[Authorize(Roles = AppRoles.GroundControl)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("demo/status")]
public sealed class DemoController(MonitoringClient monitoring, IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        try
        {
            var status = await monitoring.ReadDemoStatusAsync(ct);
            return status is null ? NotFound() : Json(status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(503, new { state = "unavailable" });
        }
    }
}
