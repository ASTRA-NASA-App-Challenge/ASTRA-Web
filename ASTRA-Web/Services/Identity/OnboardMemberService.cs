using System.Security.Claims;
using Common.Identity;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;


namespace Services.Identity;

// Used by the future ground-control admin panel. Authorization is enforced here as well as in the UI.
public sealed class OnboardMemberService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    PinLookupService lookups, PinProtectionService protection)
{
    public async Task<IdentityResult> CreateAsync(ClaimsPrincipal actor, string username, string role, string firstName,
        string lastName, string? middleName, string inputPin)
    {
        RequireGroundControl(actor);
        if (role is not (AppRoles.Crew or AppRoles.Medic))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidMemberRole", Description = "Onboard members must be crew or medics." });

        if (inputPin is null || !PinLookupService.IsPin(inputPin))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidPin", Description = "The PIN must be exactly six digits (0–9)." });
        var lookup = lookups.Compute(inputPin);
        if (await db.Users.AnyAsync(user => user.PinLookup == lookup)) return PinUnavailable();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var member = new ApplicationUser
        {
            UserName = username,
            FirstName = firstName?.Trim() ?? string.Empty,
            MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim(),
            LastName = lastName?.Trim() ?? string.Empty
        };
        try
        {
            var result = await users.CreateAsync(member, inputPin);
            if (!result.Succeeded)
            {
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
            // Another request may have reserved this PIN after the initial uniqueness check.
            await transaction.RollbackAsync();
            db.Entry(member).State = EntityState.Detached;
            return PinUnavailable();
        }
    }

    private static IdentityResult PinUnavailable() => IdentityResult.Failed(new IdentityError
    {
        Code = "PinUnavailable", Description = "This PIN is unavailable. Choose another six-digit PIN."
    });

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
