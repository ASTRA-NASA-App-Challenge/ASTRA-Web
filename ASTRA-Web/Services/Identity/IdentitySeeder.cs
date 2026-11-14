using Common.Identity;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Identity;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole(role)));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var account in configuration.GetSection("Identity:Accounts").GetChildren())
        {
            var role = account["Role"] ?? throw new InvalidOperationException("An account role is required.");
            if (role != AppRoles.GroundControl)
                throw new InvalidOperationException("Only ground control accounts can be provisioned from configuration. Use the onboard member service for crew and medics.");
            var username = account["UserName"] ?? throw new InvalidOperationException("An account username is required.");
            var user = await users.FindByNameAsync(username);
            if (user is not null)
            {
                if (user.IsGroundControl != (role == AppRoles.GroundControl)
                    || !(await users.GetRolesAsync(user)).SequenceEqual(new[] { role }))
                    throw new InvalidOperationException($"Existing account '{username}' does not match its configured role.");
                continue;
            }
            user = new ApplicationUser
            {
                UserName = username,
                IsGroundControl = role == AppRoles.GroundControl
            };
            Check(await users.CreateAsync(user, account["Password"]
                ?? throw new InvalidOperationException("An account password is required.")));
            var result = await users.AddToRoleAsync(user, role);
            if (!result.Succeeded)
            {
                Check(await users.DeleteAsync(user));
                Check(result);
            }
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
