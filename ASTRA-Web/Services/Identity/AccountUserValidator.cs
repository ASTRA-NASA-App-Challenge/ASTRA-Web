using Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace Services.Identity;

public sealed class AccountUserValidator : UserValidator<ApplicationUser>
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var result = await base.ValidateAsync(manager, user);
        if (string.IsNullOrEmpty(user.Email)) return result;
        return IdentityResult.Failed(result.Errors.Append(new IdentityError
        {
            Code = "EmailNotSupported", Description = "Accounts use a username and do not have an email address."
        }).ToArray());
    }
}
