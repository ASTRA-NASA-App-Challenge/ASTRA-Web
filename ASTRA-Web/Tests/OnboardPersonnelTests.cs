using Common.Identity;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Services.Personnel;

namespace Tests;

public class OnboardPersonnelTests
{
    [Fact]
    public async Task CountsReflectCurrentMembershipAndExcludeGroundControlAndUnassignedUsers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new OnboardPersonnelService(db);
        Assert.Equal(new Common.Models.OnboardPersonnelCounts(0, 0), await service.GetCountsAsync());

        var crew = new IdentityRole(AppRoles.Crew);
        var medic = new IdentityRole(AppRoles.Medic);
        var ground = new IdentityRole(AppRoles.GroundControl);
        db.Roles.AddRange(crew, medic, ground);
        var users = Enumerable.Range(0, 5).Select(i => new ApplicationUser { UserName = $"user-{i}" }).ToArray();
        db.Users.AddRange(users);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = users[0].Id, RoleId = crew.Id },
            new IdentityUserRole<string> { UserId = users[1].Id, RoleId = crew.Id },
            new IdentityUserRole<string> { UserId = users[2].Id, RoleId = medic.Id },
            new IdentityUserRole<string> { UserId = users[3].Id, RoleId = ground.Id });
        await db.SaveChangesAsync();
        Assert.Equal(new Common.Models.OnboardPersonnelCounts(2, 1), await service.GetCountsAsync());

        // Later requests must reflect a role reassignment without a restart or a cached count.
        db.UserRoles.Remove(await db.UserRoles.SingleAsync(m => m.UserId == users[1].Id));
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = users[1].Id, RoleId = medic.Id });
        await db.SaveChangesAsync();
        Assert.Equal(new Common.Models.OnboardPersonnelCounts(1, 2), await service.GetCountsAsync());
    }
}
