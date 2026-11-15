using System.Security.Claims;
using Common.Identity;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Services.Identity;

public sealed record DemoMember(string Username, string FirstName, string LastName, string Role, string Pin);
public static class DemoAccounts
{
    public static IReadOnlyList<DemoMember> Members { get; } = Array.AsReadOnly(new[]
    {
        new DemoMember("Medic", "Maya", "Chen", AppRoles.Medic, "410201"),
        new DemoMember("dr.reed", "Samuel", "Reed", AppRoles.Medic, "410202"),
        new DemoMember("Crew", "Alex", "Morgan", AppRoles.Crew, "520301"),
        new DemoMember("jordan.lee", "Jordan", "Lee", AppRoles.Crew, "520302"),
        new DemoMember("sofia.patel", "Sofia", "Patel", AppRoles.Crew, "520303"),
        new DemoMember("noah.rivera", "Noah", "Rivera", AppRoles.Crew, "520304")
    });

    public static async Task SeedAsync(IServiceProvider services, bool replaceLegacy = false)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (replaceLegacy)
        {
            var ground = await users.FindByNameAsync("ASTRA Ground Control");
            if (ground is null || !ground.IsGroundControl || !await users.IsInRoleAsync(ground, AppRoles.GroundControl))
                throw new InvalidOperationException("The existing ground-control account is required for demo preparation.");
            var actor = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, ground.Id), new Claim(ClaimTypes.Role, AppRoles.GroundControl) }, "DemoPreparation"));
            var onboard = services.GetRequiredService<OnboardMemberService>();
            foreach (var name in new[] { "salex", "ASTRA Test Astronaut" })
            {
                var legacy = await users.FindByNameAsync(name);
                if (legacy is not null) Check(await onboard.DeleteAsync(actor, legacy.Id));
            }
        }
        foreach (var account in Members)
        {
            // Never reset credentials or roles on subsequent startups.
            if (await users.FindByNameAsync(account.Username) is not null) continue;
            var lookup = services.GetRequiredService<PinLookupService>().Compute(account.Pin);
            var db = services.GetRequiredService<ApplicationDbContext>();
            if (await db.MonitoringPinReservations.AnyAsync(x => x.PinLookup == lookup)
                || await users.Users.AnyAsync(x => x.PinLookup == lookup)) continue;
            var member = new ApplicationUser { UserName = account.Username, FirstName = account.FirstName, LastName = account.LastName };
            Check(await users.CreateAsync(member, account.Pin));
            var result = await users.AddToRoleAsync(member, account.Role);
            if (!result.Succeeded) { Check(await users.DeleteAsync(member)); Check(result); }
        }
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
