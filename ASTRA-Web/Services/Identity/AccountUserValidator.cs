using Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace Services.Identity;

public sealed class AccountUserValidator : UserValidator<ApplicationUser>
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var result = await base.ValidateAsync(manager, user);
        var errors = result.Errors.ToList();
        if (!string.IsNullOrEmpty(user.Email)) errors.Add(new IdentityError
        {
            Code = "EmailNotSupported", Description = "Accounts use a username and do not have an email address."
        });
        if (!user.IsGroundControl)
        {
            if (string.IsNullOrWhiteSpace(user.FirstName))
                errors.Add(new IdentityError { Code = "FirstNameRequired", Description = "First name is required for crew and medics." });
            if (string.IsNullOrWhiteSpace(user.LastName))
                errors.Add(new IdentityError { Code = "LastNameRequired", Description = "Last name is required for crew and medics." });
        }
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
}
