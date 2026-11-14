using System.ComponentModel.DataAnnotations;
using Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Identity;

namespace Web.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class IndexModel(UserManager<ApplicationUser> users) : PageModel
{
    [BindProperty]
    public AccountInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        Input = new AccountInput
        {
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await users.SetEmailAsync(user, Input.Email.Trim());
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        return RedirectToPage();
    }

    public class AccountInput
    {
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
