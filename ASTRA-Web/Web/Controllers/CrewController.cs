using Common.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Identity;
using Web.Models;

namespace Web.Controllers;

[Authorize(Roles = AppRoles.GroundControl)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CrewController(OnboardMemberService members) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new CrewOverviewViewModel { Members = await members.GetMembersAsync(User, cancellationToken) });

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

        return View("Index", new CrewOverviewViewModel
        {
            Members = await members.GetMembersAsync(User, cancellationToken),
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
        return View("Index", new CrewOverviewViewModel
        {
            Members = await members.GetMembersAsync(User, cancellationToken),
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
        return View("Index", new CrewOverviewViewModel
        {
            Members = await members.GetMembersAsync(User, cancellationToken),
            DeletedMember = input,
            DeleteMemberName = member.FullName,
            OpenDeleteMember = true
        });
    }
}
