using Common.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Identity;
using Web.Models;
using Web.Monitoring;

namespace Web.Controllers;

[Authorize(Roles = AppRoles.Medic)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MedicController(MedicMemberService members, CrewHealthReader health, MonitoringClient monitoring) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var viewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roster = (await members.GetMembersAsync(User, cancellationToken)).Where(x => x.Id != viewerId).ToList();
        var readings = await health.ReadRosterAsync(roster, cancellationToken);
        return View("~/Views/Crew/Index.cshtml", new CrewOverviewViewModel
        {
            IsMedicView = true,
            Members = roster.Select(x => x with { Pin = null }).ToList(),
            Health = readings.ToDictionary(x => x.Key, x => ClinicalView(x.Value))
        });
    }

    public async Task<IActionResult> Health(string id, CancellationToken cancellationToken, int days = 7)
    {
        var member = (await members.GetMembersAsync(User, cancellationToken)).FirstOrDefault(x => x.Id == id);
        if (member is null) return NotFound();
        return View("~/Views/Home/Dashboard.cshtml", ClinicalView(await health.ReadAsync(member, days == 30 ? 30 : 7, false, cancellationToken)));
    }

    public async Task<IActionResult> HealthPhoto(string memberId, string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) return BadRequest();
        var member = (await members.GetMembersAsync(User, cancellationToken)).FirstOrDefault(x => x.Id == memberId);
        if (member?.Pin is null) return NotFound();
        try
        {
            var photo = await monitoring.PhotoAsync(member.Pin, id, cancellationToken);
            return photo is null ? NotFound() : File(photo, "image/jpeg");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return NotFound(); }
    }

    private static CrewDashboardModel ClinicalView(CrewDashboardModel source) => new()
    {
        MemberController = "Medic", MemberId = source.MemberId, MemberRole = source.MemberRole,
        Name = source.Name, Username = source.Username, Days = source.Days,
        Record = source.Record, Error = source.Error, Now = source.Now
    };
}
