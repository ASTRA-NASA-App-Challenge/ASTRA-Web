using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Identity;

namespace Web.Areas.Identity.Pages.Account;

[Authorize(Roles = AppRoles.Crew + "," + AppRoles.Medic)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MyPinModel(OnboardMemberService members) : PageModel
{
    public string? Pin { get; private set; }
    public async Task OnGetAsync()
    {
        Pin = await members.GetPinAsync(User, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
