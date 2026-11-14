using System.ComponentModel.DataAnnotations;
using Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Identity;

namespace Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
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
            if (result.RequiresTwoFactor)
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = false });
        }
        ModelState.AddModelError(string.Empty, "Login failed. Check your credentials or try again later.");
        return Page();
    }
}
