using System.Security.Claims;
using System.Security.Cryptography;
using Common.Identity;
using Common.Models;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Services.Identity;

// Decrypted PINs are server-side monitoring lookup keys, not clinical UI data.
public sealed class MedicMemberService(ApplicationDbContext db, PinProtectionService protection)
{
    public async Task<IReadOnlyList<OnboardMemberSummary>> GetMembersAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        if (actor.Identity?.IsAuthenticated != true || !actor.IsInRole(AppRoles.Medic))
            throw new UnauthorizedAccessException("Only onboard medics can access the clinical roster.");
        var records = await (
            from member in db.Users.AsNoTracking()
            join membership in db.UserRoles on member.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            where !member.IsGroundControl && (role.Name == AppRoles.Crew || role.Name == AppRoles.Medic)
                && !db.UserRoles.Any(link => link.UserId == member.Id && db.Roles.Any(r => r.Id == link.RoleId && r.Name == AppRoles.GroundControl))
            orderby member.FirstName, member.LastName
            select new { member.Id, member.UserName, member.FirstName, member.MiddleName, member.LastName, member.ProtectedPin, Role = role.Name! }
        ).ToListAsync(ct);
        return records.GroupBy(x => x.Id).Select(group =>
        {
            var member = group.FirstOrDefault(x => x.Role == AppRoles.Medic) ?? group.First();
            string? pin = null;
            if (member.ProtectedPin is not null)
            {
                try { pin = protection.Reveal(member.Id, member.ProtectedPin); }
                catch (CryptographicException) { }
            }
            return new OnboardMemberSummary(member.Id, member.UserName ?? "", member.FirstName, member.MiddleName, member.LastName, member.Role, pin);
        }).ToList();
    }
}
