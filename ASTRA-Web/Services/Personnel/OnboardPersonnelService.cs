using Common.Identity;
using Common.Models;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Services.Personnel;

public sealed class OnboardPersonnelService(ApplicationDbContext db) : IOnboardPersonnelService
{
    public async Task<OnboardPersonnelCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await (
            from membership in db.UserRoles
            join user in db.Users on membership.UserId equals user.Id
            join role in db.Roles on membership.RoleId equals role.Id
            where role.Name == AppRoles.Crew || role.Name == AppRoles.Medic
            group user.Id by role.Name into members
            select new { Role = members.Key!, Count = members.Distinct().Count() }
        ).ToDictionaryAsync(item => item.Role, item => item.Count, cancellationToken);

        return new OnboardPersonnelCounts(counts.GetValueOrDefault(AppRoles.Crew), counts.GetValueOrDefault(AppRoles.Medic));
    }
}
