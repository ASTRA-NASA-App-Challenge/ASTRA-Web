using System.ComponentModel.DataAnnotations;
using Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IConfiguration configuration) : PageModel
{
    public bool DemoEnabled => configuration.GetValue<bool>("Demo:Enabled");
    public async Task<IActionResult> OnPostDemoAsync(string profile)
    {
        if (!DemoEnabled) return NotFound();
        var (username, role) = profile switch
        {
            "ground" => ("ASTRA Ground Control", Common.Identity.AppRoles.GroundControl),
            "medic" => ("Medic", Common.Identity.AppRoles.Medic),
            "crew" => ("Crew", Common.Identity.AppRoles.Crew),
            _ => ("", "")
        };
        if (username.Length == 0) return BadRequest();
        var user = await users.FindByNameAsync(username);
        if (user is null || !await users.IsInRoleAsync(user, role)) return NotFound();
        // Explicit demo-only access to three whitelisted profiles; never enabled by default in production.
        await signIn.SignInAsync(user, isPersistent: false);
        return LocalRedirect(Url.Content("~/"));
    }
    [BindProperty] public LoginInput Input { get; set; } = new();

    public class LoginInput
    {
        [Required] public string Identifier { get; set; } = "";
        [Required] public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid) return Page();
        var identifier = Input.Identifier.Trim();
        var user = await users.FindByNameAsync(identifier);
        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(user, Input.Password, false, lockoutOnFailure: true);
            if (result.Succeeded)
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/"));
        }
        ModelState.AddModelError(string.Empty, "Login failed. Check your credentials or try again later.");
        return Page();
    }
}
