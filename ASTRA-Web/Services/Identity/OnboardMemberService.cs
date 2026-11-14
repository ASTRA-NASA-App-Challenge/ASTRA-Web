using System.Security.Claims;
using Common.Identity;
using Common.Models;
using System.Security.Cryptography;
using System.Globalization;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;


namespace Services.Identity;

// Authorization is enforced here as well as in the ground-control UI.
public sealed class OnboardMemberService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    PinLookupService lookups, PinProtectionService protection)
{
    public async Task<IReadOnlyList<OnboardMemberSummary>> GetMembersAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        RequireGroundControl(actor);
        var memberships = await (
            from member in db.Users.AsNoTracking()
            join membership in db.UserRoles on member.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where !member.IsGroundControl && (role.Name == AppRoles.Crew || role.Name == AppRoles.Medic)
            orderby member.FirstName, member.LastName, member.UserName
            select new { member.Id, member.UserName, member.FirstName, member.MiddleName, member.LastName, member.ProtectedPin, Role = role.Name! }
        ).ToListAsync(cancellationToken);

        return memberships.GroupBy(member => member.Id).Select(group =>
        {
            var member = group.FirstOrDefault(item => item.Role == AppRoles.Medic) ?? group.First();
            string? pin = null;
            if (member.ProtectedPin is not null)
            {
                try { pin = protection.Reveal(member.Id, member.ProtectedPin); }
                catch (CryptographicException) { /* Keep the roster usable when an old PIN cannot be decrypted. */ }
            }
            return new OnboardMemberSummary(member.Id, member.UserName ?? string.Empty, member.FirstName,
                member.MiddleName, member.LastName, member.Role, pin);
        }).ToList();
    }

    public async Task<IdentityResult> CreateAsync(ClaimsPrincipal actor, string username, string role, string firstName,
        string lastName, string? middleName)
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
            var member = new ApplicationUser
            {
                UserName = username,
                FirstName = firstName?.Trim() ?? string.Empty,
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim(),
                LastName = lastName?.Trim() ?? string.Empty
            };
            try
            {
                var result = await users.CreateAsync(member, pin);
                if (!result.Succeeded)
                {
                    if (result.Errors.All(error => error.Code == "PinUnavailable")) continue;
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
                continue;
            }
        }
        return IdentityResult.Failed(new IdentityError
        {
            Code = "PinGenerationFailed", Description = "Unable to allocate a unique PIN. Please try again."
        });
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

    public async Task<IdentityResult> UpdateAsync(ClaimsPrincipal actor, string memberId, string username,
        string role, string firstName, string lastName, string? middleName)
    {
        RequireGroundControl(actor);
        if (role is not (AppRoles.Crew or AppRoles.Medic))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidMemberRole", Description = "Onboard members must be crew or medics." });
        var member = await FindOnboardMemberAsync(memberId);
        if (member is null) return MemberNotFound();

        var roles = await users.GetRolesAsync(member);
        var previousRoles = roles.Where(oldRole => (oldRole is AppRoles.Crew or AppRoles.Medic) && oldRole != role).ToArray();
        var updatedUsername = username?.Trim() ?? string.Empty;
        var accessChanged = member.UserName != updatedUsername || previousRoles.Length > 0 || !roles.Contains(role);
        await using var transaction = await db.Database.BeginTransactionAsync();
        member.UserName = updatedUsername;
        member.FirstName = firstName?.Trim() ?? string.Empty;
        member.MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim();
        member.LastName = lastName?.Trim() ?? string.Empty;

        var result = await users.UpdateAsync(member);
        if (result.Succeeded && previousRoles.Length > 0)
            result = await users.RemoveFromRolesAsync(member, previousRoles);
        if (result.Succeeded && !roles.Contains(role))
            result = await users.AddToRoleAsync(member, role);
        if (result.Succeeded && accessChanged)
            result = await users.UpdateSecurityStampAsync(member);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            foreach (var entry in db.ChangeTracker.Entries<IdentityUserRole<string>>().Where(entry => entry.Entity.UserId == member.Id).ToList())
                entry.State = EntityState.Detached;
            db.Entry(member).State = EntityState.Detached;
            return result;
        }
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ClaimsPrincipal actor, string memberId)
    {
        RequireGroundControl(actor);
        var member = await FindOnboardMemberAsync(memberId);
        return member is null ? MemberNotFound() : await users.DeleteAsync(member);
    }

    private async Task<ApplicationUser?> FindOnboardMemberAsync(string memberId)
    {
        var member = await users.FindByIdAsync(memberId);
        if (member is null || member.IsGroundControl) return null;
        var roles = await users.GetRolesAsync(member);
        return !roles.Contains(AppRoles.GroundControl) && roles.Any(role => role is AppRoles.Crew or AppRoles.Medic) ? member : null;
    }

    private static IdentityResult MemberNotFound() => IdentityResult.Failed(new IdentityError
    {
        Code = "MemberNotFound", Description = "The onboard member could not be found. Refresh the page and try again."
    });

    private static void RequireGroundControl(ClaimsPrincipal actor)
    {
        if (actor.Identity?.IsAuthenticated != true || !actor.IsInRole(AppRoles.GroundControl))
            throw new UnauthorizedAccessException("Only ground control can manage onboard members.");
    }
}
