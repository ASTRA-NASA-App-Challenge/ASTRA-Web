using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Web.Data;

namespace Web.Identity;

// Used by the future ground-control admin panel. Authorization is enforced here as well as in the UI.
public sealed class OnboardMemberService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    PinLookupService lookups, PinProtectionService protection)
{
    public async Task<IdentityResult> CreateAsync(ClaimsPrincipal actor, string username, string email, string role)
    {
        RequireGroundControl(actor);
        if (role is not (AppRoles.Crew or AppRoles.Medic))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidMemberRole", Description = "Onboard members must be crew or medics." });

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var pin = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            var lookup = lookups.Compute(pin);
            if (await db.Users.AnyAsync(user => user.PinLookup == lookup)) continue;

            await using var transaction = await db.Database.BeginTransactionAsync();
            var member = new ApplicationUser { UserName = username, Email = email, EmailConfirmed = true };
            try
            {
                var result = await users.CreateAsync(member, pin);
                if (!result.Succeeded)
                {
                    if (result.Errors.Any(error => error.Code == "PinUnavailable")) continue;
                    return result;
                }
                result = await users.AddToRoleAsync(member, role);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    db.Entry(member).State = EntityState.Detached;
                    return result;
                }
                await transaction.CommitAsync();
                return IdentityResult.Success;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqliteException
                { SqliteExtendedErrorCode: 2067 } sqlite && sqlite.Message.Contains("AspNetUsers.PinLookup", StringComparison.Ordinal))
            {
                // A simultaneous request reserved this PIN. Let the database enforce uniqueness and try again.
                await transaction.RollbackAsync();
                db.Entry(member).State = EntityState.Detached;
            }
        }
        return IdentityResult.Failed(new IdentityError { Code = "PinGenerationFailed", Description = "Unable to allocate a unique PIN. Try again." });
    }

    public async Task<string?> GetPinAsync(ClaimsPrincipal actor, string memberId)
    {
        var isGround = actor.Identity?.IsAuthenticated == true && actor.IsInRole(AppRoles.GroundControl);
        var isOwner = actor.Identity?.IsAuthenticated == true
            && (actor.IsInRole(AppRoles.Crew) || actor.IsInRole(AppRoles.Medic))
            && actor.FindFirstValue(ClaimTypes.NameIdentifier) == memberId;
        if (!isGround && !isOwner) throw new UnauthorizedAccessException("Only the member or ground control can view this PIN.");

        var member = await users.FindByIdAsync(memberId);
        if (member is null || member.IsGroundControl || member.ProtectedPin is null) return null;
        return protection.Reveal(member.Id, member.ProtectedPin);
    }

    private static void RequireGroundControl(ClaimsPrincipal actor)
    {
        if (actor.Identity?.IsAuthenticated != true || !actor.IsInRole(AppRoles.GroundControl))
            throw new UnauthorizedAccessException("Only ground control can create onboard members.");
    }
}
