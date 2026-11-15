using Common.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Identity;
using Web.Models;

namespace Web.Controllers;

[Authorize(Roles = AppRoles.GroundControl)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CrewController(OnboardMemberService members, Web.Monitoring.CrewHealthReader health, Web.Monitoring.MonitoringClient monitoring) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var roster = await members.GetMembersAsync(User, cancellationToken);
        return View(new CrewOverviewViewModel { Members = roster, Health = await health.ReadRosterAsync(roster, cancellationToken) });
    }

    public async Task<IActionResult> Health(string id, CancellationToken cancellationToken, int days = 7)
    {
        var member = (await members.GetMembersAsync(User, cancellationToken)).FirstOrDefault(x => x.Id == id);
        if (member is null) return NotFound();
        return View("~/Views/Home/Dashboard.cshtml", await health.ReadAsync(member, days == 30 ? 30 : 7, false, cancellationToken));
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "NewMember")] AddMemberInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await members.CreateAsync(User, input.Username.Trim(), input.Role,
                input.FirstName, input.LastName, input.MiddleName);
            if (result.Succeeded)
            {
                TempData["CrewNotice"] = $"{input.FirstName.Trim()} {input.LastName.Trim()} was added to the onboard team.";
                var roster = await members.GetMembersAsync(User, cancellationToken);
                var created = roster.Single(x => x.Username == input.Username.Trim());
                try
                {
                    if (created.Pin is not null)
                        await HttpContext.RequestServices.GetRequiredService<Web.Monitoring.MonitoringClient>().RegisterAsync(created.Pin, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or AggregateException)
                {
                    TempData["CrewNotice"] += " Monitoring registration is pending and will retry automatically.";
                }
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors)
            {
                var field = error.Code switch
                {
                    "FirstNameRequired" => "FirstName",
                    "LastNameRequired" => "LastName",
                    "DuplicateUserName" or "InvalidUserName" => "Username",
                    "InvalidMemberRole" => "Role",
                    _ => null
                };
                ModelState.AddModelError(field is null ? string.Empty : $"NewMember.{field}", error.Description);
            }
        }

        var refreshedRoster = await members.GetMembersAsync(User, cancellationToken);
        return View("Index", new CrewOverviewViewModel
        {
            Members = refreshedRoster,
            Health = await health.ReadRosterAsync(refreshedRoster, cancellationToken),
            NewMember = input,
            OpenAddMember = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "EditedMember")] EditMemberInput input, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await members.UpdateAsync(User, input.Id, input.Username, input.Role,
                input.FirstName, input.LastName, input.MiddleName);
            if (result.Succeeded)
            {
                TempData["CrewNotice"] = $"{input.FirstName.Trim()} {input.LastName.Trim()} was updated.";
                return RedirectToAction(nameof(Index));
            }
            if (result.Errors.Any(error => error.Code == "MemberNotFound")) return NotFound();
            foreach (var error in result.Errors)
            {
                var field = error.Code switch
                {
                    "FirstNameRequired" => "FirstName",
                    "LastNameRequired" => "LastName",
                    "DuplicateUserName" or "InvalidUserName" => "Username",
                    "InvalidMemberRole" => "Role",
                    _ => null
                };
                ModelState.AddModelError(field is null ? string.Empty : $"EditedMember.{field}", error.Description);
            }
        }
        var refreshedRoster = await members.GetMembersAsync(User, cancellationToken);
        return View("Index", new CrewOverviewViewModel
        {
            Members = refreshedRoster,
            Health = await health.ReadRosterAsync(refreshedRoster, cancellationToken),
            EditedMember = input,
            OpenEditMember = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([Bind(Prefix = "DeletedMember")] DeleteMemberInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var roster = await members.GetMembersAsync(User, cancellationToken);
        var member = roster.FirstOrDefault(member => member.Id == input.Id);
        if (member is null) return NotFound();
        var result = await members.DeleteAsync(User, input.Id);
        if (result.Succeeded)
        {
            TempData["CrewNotice"] = $"{member.FullName} was removed from the onboard team.";
            try { await HttpContext.RequestServices.GetRequiredService<Web.Monitoring.MonitoringDeletionProcessor>().ProcessAsync(cancellationToken); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or AggregateException)
            { TempData["CrewNotice"] += " Monitoring cleanup is queued and will retry automatically."; }
            return RedirectToAction(nameof(Index));
        }
        if (result.Errors.Any(error => error.Code == "MemberNotFound")) return NotFound();
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        var refreshedRoster = await members.GetMembersAsync(User, cancellationToken);
        return View("Index", new CrewOverviewViewModel
        {
            Members = refreshedRoster,
            Health = await health.ReadRosterAsync(refreshedRoster, cancellationToken),
            DeletedMember = input,
            DeleteMemberName = member.FullName,
            OpenDeleteMember = true
        });
    }
}
