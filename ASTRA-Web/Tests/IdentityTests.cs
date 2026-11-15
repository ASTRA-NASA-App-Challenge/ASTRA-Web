using System.Security.Claims;
using Common.Identity;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Web.Identity;
using Microsoft.AspNetCore.DataProtection;
using Services.Identity;

namespace Tests;

public class IdentityTests
{
    [Fact]
    public async Task RosterIsGroundControlOnlyAndListsOnboardMembersWithTheirOwnPins()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var actor = Principal("ground", AppRoles.GroundControl);
        Assert.Empty(await members.GetMembersAsync(actor));
        Assert.True((await members.CreateAsync(actor, "alex", AppRoles.Crew, "Alex", "Morgan", null)).Succeeded);
        Assert.True((await members.CreateAsync(actor, "sam", AppRoles.Medic, "Sam", "Reed", "Avery")).Succeeded);
        var ground = new ApplicationUser { UserName = "ground", IsGroundControl = true };
        Assert.True((await users.CreateAsync(ground, "Ground-Control!2026")).Succeeded);
        Assert.True((await users.AddToRoleAsync(ground, AppRoles.GroundControl)).Succeeded);
        var roster = await members.GetMembersAsync(actor);
        Assert.Equal(2, roster.Count);
        Assert.Equal("Alex Morgan", roster[0].FullName);
        Assert.Equal("AM", roster[0].Initials);
        Assert.True(PinLookupService.IsPin(roster[0].Pin!));
        Assert.False(roster[0].IsMedic);
        Assert.Equal("Sam Avery Reed", roster[1].FullName);
        Assert.True(PinLookupService.IsPin(roster[1].Pin!));
        Assert.NotEqual(roster[0].Pin, roster[1].Pin);
        Assert.True(roster[1].IsMedic);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetMembersAsync(Principal("alex", AppRoles.Crew)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetMembersAsync(Principal("sam", AppRoles.Medic)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetMembersAsync(new ClaimsPrincipal()));

        var medic = (await users.FindByNameAsync("sam"))!;
        medic.ProtectedPin = "unreadable-old-pin";
        Assert.True((await users.UpdateAsync(medic)).Succeeded);
        roster = await members.GetMembersAsync(actor);
        Assert.Equal(2, roster.Count);
        Assert.Null(roster.Single(member => member.Username == "sam").Pin);
    }

    [Fact]
    public async Task AccountCleanupDropsContactFieldsAndPreservesCredentialsAndRoleMembership()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261114130044_AddMemberNames");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "FirstName", "LastName", "PasswordHash", "PinLookup", "ProtectedPin",
                "Email", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "AccessFailedCount", "LockoutEnabled", "IsGroundControl")
            VALUES ('crew', 'alex', 'Alex', 'Morgan', 'password-hash', 'unique-lookup', 'encrypted-pin', 'unused@example.test', 1, 0, 0, 0, 1, 0);
            INSERT INTO "AspNetRoles" ("Id", "Name") VALUES ('crew-role', 'Crew');
            INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES ('crew', 'crew-role');
            """);
        await migrator.MigrateAsync();
        var user = await db.Users.SingleAsync();
        Assert.Equal("Alex", user.FirstName);
        Assert.Equal("Morgan", user.LastName);
        Assert.Equal("password-hash", user.PasswordHash);
        Assert.Equal("encrypted-pin", user.ProtectedPin);
        Assert.Equal("unique-lookup", user.PinLookup);
        Assert.Single(await db.UserRoles.ToListAsync());

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('AspNetUsers')";
        using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        foreach (var removed in new[] { "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled" })
        {
            Assert.DoesNotContain(removed, columns);
            Assert.Null(db.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty(removed));
        }
    }

    [Theory]
    [InlineData(AppRoles.Crew, null, "Morgan", "FirstNameRequired")]
    [InlineData(AppRoles.Crew, "  ", "Morgan", "FirstNameRequired")]
    [InlineData(AppRoles.Crew, "Alex", null, "LastNameRequired")]
    [InlineData(AppRoles.Medic, null, "Morgan", "FirstNameRequired")]
    [InlineData(AppRoles.Medic, "Alex", "  ", "LastNameRequired")]
    public async Task OnboardAccountsRequireFirstAndLastNames(string role, string? firstName, string? lastName, string error)
    {
        using var fixture = new IdentityFixture();
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var result = await members.CreateAsync(Principal("ground", AppRoles.GroundControl), "incomplete", role,
            firstName!, lastName!, null);
        Assert.Contains(result.Errors, item => item.Code == error);
        Assert.Empty(await fixture.Services.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task GeneratedPinsAreUniqueAndAuthenticateWithoutResettingExistingAccounts()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var actor = Principal("ground", AppRoles.GroundControl);
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var pins = new HashSet<string>();
        for (var index = 0; index < 6; index++)
        {
            var role = index % 2 == 0 ? AppRoles.Crew : AppRoles.Medic;
            Assert.True((await members.CreateAsync(actor, $"member-{index}", role, "Alex", "Morgan", null)).Succeeded);
            var user = (await users.FindByNameAsync($"member-{index}"))!;
            var pin = (await members.GetPinAsync(actor, user.Id))!;
            Assert.True(PinLookupService.IsPin(pin));
            Assert.True(pins.Add(pin));
            Assert.True(await users.CheckPasswordAsync(user, pin));
            Assert.NotEqual(pin, user.PasswordHash);
            Assert.NotEqual(pin, user.ProtectedPin);
        }
        var original = (await users.FindByNameAsync("member-0"))!;
        var originalPin = await members.GetPinAsync(actor, original.Id);
        var duplicate = await members.CreateAsync(actor, "member-0", AppRoles.Crew, "Taylor", "Lee", null);
        Assert.Contains(duplicate.Errors, error => error.Code == "DuplicateUserName");
        Assert.Equal(originalPin, await members.GetPinAsync(actor, original.Id));
        Assert.Equal(6, await users.Users.CountAsync());
    }

    [Fact]
    public async Task CrewPinsAreSixDigitsUniqueAndHashedAndCanBeChanged()
    {
        using var fixture = new IdentityFixture();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var crew = new ApplicationUser { UserName = "crew", FirstName = "Alex", LastName = "Morgan" };
        Assert.False((await users.CreateAsync(crew, "12345")).Succeeded);
        Assert.False((await users.CreateAsync(crew, "１２３４５６")).Succeeded);
        Assert.True((await users.CreateAsync(crew, "012345")).Succeeded);
        Assert.NotEqual("012345", crew.PasswordHash);
        Assert.NotEqual("012345", crew.PinLookup);
        Assert.True(await users.CheckPasswordAsync(crew, "012345"));
        Assert.False(await users.CheckPasswordAsync(crew, "123456"));
        var medic = new ApplicationUser { UserName = "medic", FirstName = "Sam", LastName = "Reed" };
        Assert.False((await users.CreateAsync(medic, "012345")).Succeeded);
        Assert.True((await users.CreateAsync(medic, "654321")).Succeeded);
        Assert.False((await users.ChangePasswordAsync(crew, "012345", "654321")).Succeeded);
        Assert.True((await users.ChangePasswordAsync(crew, "012345", "111111")).Succeeded);
        var next = new ApplicationUser { UserName = "next", FirstName = "Taylor", LastName = "Lee" };
        Assert.True((await users.CreateAsync(next, "012345")).Succeeded);
    }

    [Fact]
    public async Task GroundControlRequiresConventionalPasswordAndNoPin()
    {
        using var fixture = new IdentityFixture();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = new ApplicationUser { UserName = "ground", IsGroundControl = true };
        Assert.False((await users.CreateAsync(ground, "123456")).Succeeded);
        Assert.True((await users.CreateAsync(ground, "Ground-Control!2026")).Succeeded);
        Assert.Null(ground.PinLookup);
        Assert.True(await users.CheckPasswordAsync(ground, "Ground-Control!2026"));
    }

    [Theory]
    [InlineData(AppRoles.Crew, "self", true)]
    [InlineData(AppRoles.Crew, "someone-else", false)]
    [InlineData(AppRoles.Medic, "someone-else", true)]
    [InlineData(AppRoles.GroundControl, "someone-else", true)]
    [InlineData("Unknown", "self", false)]
    [InlineData(AppRoles.Medic, "", false)]
    public async Task MedicalRecordAccessIsRestricted(string role, string patient, bool allowed)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "self"), new Claim(ClaimTypes.Role, role)
        ], "test"));
        var requirement = new ReadMedicalRecordRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, patient);
        await new MedicalRecordAuthorizationHandler().HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
    }

    [Fact]
    public async Task AnonymousUsersCannotReadRecordsEvenWithRoleClaim()
    {
        var requirement = new ReadMedicalRecordRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.Medic)]));
        var context = new AuthorizationHandlerContext([requirement], user, "patient");
        await new MedicalRecordAuthorizationHandler().HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task MigrationPreservesExistingIdentityUsers()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var legacy = new LegacyContext(new DbContextOptionsBuilder<LegacyContext>().UseSqlite(connection).Options))
        {
            await legacy.Database.EnsureCreatedAsync();
            legacy.Users.Add(new IdentityUser { Id = "legacy", UserName = "existing", Email = "old@example.test", NormalizedEmail = "OLD@EXAMPLE.TEST", EmailConfirmed = true });
            await legacy.SaveChangesAsync();
        }
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await IdentityDatabaseInitializer.InitializeAsync(db);
        Assert.Equal("existing", (await db.Users.SingleAsync()).UserName);
        Assert.Null((await db.Users.SingleAsync()).Email);
        Assert.Null((await db.Users.SingleAsync()).NormalizedEmail);
        Assert.False((await db.Users.SingleAsync()).EmailConfirmed);
        Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetAppliedMigrationsAsync()).Count());
        await IdentityDatabaseInitializer.InitializeAsync(db);
        Assert.Single(await db.Users.ToListAsync());
    }

    private sealed class LegacyContext(DbContextOptions<LegacyContext> options)
        : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext(options);

    [Fact]
    public async Task ProvisioningSeedsRolesAndAccountsWithoutResettingPasswords()
    {
        using var fixture = new IdentityFixture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Accounts:0:UserName"] = "mission-ground",
            ["Identity:Accounts:0:Role"] = AppRoles.GroundControl,
            ["Identity:Accounts:0:Password"] = "Ground-Control!2026"
        }).Build();
        await IdentitySeeder.SeedAsync(fixture.Services, configuration);
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = (await users.FindByNameAsync("mission-ground"))!;
        Assert.Null(ground.Email);
        Assert.True((await users.ChangePasswordAsync(ground, "Ground-Control!2026", "Changed-Ground!2026")).Succeeded);
        await IdentitySeeder.SeedAsync(fixture.Services, configuration);
        Assert.True(await users.CheckPasswordAsync(ground, "Changed-Ground!2026"));
        Assert.True(ground.IsGroundControl);
        Assert.Null(ground.PinLookup);
        Assert.True(await users.IsInRoleAsync(ground, AppRoles.GroundControl));
        Assert.Equal(1, await users.Users.CountAsync());
    }

    [Fact]
    public async Task GroundControlCreatesMembersWithGeneratedRecoverableUniquePins()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(ground, "crew", AppRoles.Crew, " Alex ", " Morgan ", null)).Succeeded);
        Assert.True((await members.CreateAsync(ground, "medic", AppRoles.Medic, "Sam", "Reed", " Avery ")).Succeeded);
        var crew = (await users.FindByNameAsync("crew"))!;
        var medic = (await users.FindByNameAsync("medic"))!;
        Assert.Null(crew.Email);
        Assert.Null(medic.Email);
        Assert.Equal("Alex", crew.FirstName);
        Assert.Equal("Morgan", crew.LastName);
        Assert.Null(crew.MiddleName);
        Assert.Equal("Avery", medic.MiddleName);
        var pin = (await members.GetPinAsync(ground, crew.Id))!;
        Assert.True(PinLookupService.IsPin(pin));
        Assert.True(await users.CheckPasswordAsync(crew, pin));
        Assert.NotEqual(pin, crew.ProtectedPin);
        var medicPin = await members.GetPinAsync(ground, medic.Id);
        Assert.NotEqual(pin, medicPin);
        Assert.Equal(pin, await members.GetPinAsync(Principal(crew.Id, AppRoles.Crew), crew.Id));
        Assert.True(await users.IsInRoleAsync(crew, AppRoles.Crew));
        Assert.True(await users.IsInRoleAsync(medic, AppRoles.Medic));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetPinAsync(Principal(medic.Id, AppRoles.Medic), crew.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetPinAsync(Principal(crew.Id, AppRoles.Crew), medic.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetPinAsync(new ClaimsPrincipal(), crew.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.CreateAsync(Principal(medic.Id, AppRoles.Medic), "unauthorized", AppRoles.Crew, "", "", null));
        Assert.False((await members.CreateAsync(ground, "invalid", AppRoles.GroundControl, "", "", null)).Succeeded);
        var replacement = new[] { "012345", "012346", "012347" }.First(candidate => candidate != pin && candidate != medicPin);
        Assert.True((await users.ChangePasswordAsync(crew, pin, replacement)).Succeeded);
        Assert.Equal(replacement, await members.GetPinAsync(ground, crew.Id));
    }

    [Fact]
    public async Task EditingMemberUpdatesRoleAndDetailsWithoutChangingPin()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(ground, "crew", AppRoles.Crew, "Alex", "Morgan", "Lee")).Succeeded);
        var member = (await users.FindByNameAsync("crew"))!;
        var pin = await members.GetPinAsync(ground, member.Id);
        var hash = member.PasswordHash;
        var protectedPin = member.ProtectedPin;
        var lookup = member.PinLookup;
        var stamp = member.SecurityStamp;

        Assert.True((await members.UpdateAsync(ground, member.Id, " medic ", AppRoles.Medic, " Sam ", " Reed ", " ")).Succeeded);
        var updated = (await users.FindByNameAsync("medic"))!;
        Assert.Equal("Sam", updated.FirstName);
        Assert.Equal("Reed", updated.LastName);
        Assert.Null(updated.MiddleName);
        Assert.Equal(new[] { AppRoles.Medic }, await users.GetRolesAsync(updated));
        Assert.NotEqual(stamp, updated.SecurityStamp);
        Assert.Equal(hash, updated.PasswordHash);
        Assert.Equal(protectedPin, updated.ProtectedPin);
        Assert.Equal(lookup, updated.PinLookup);
        Assert.Equal(pin, await members.GetPinAsync(ground, member.Id));
        Assert.True(await users.CheckPasswordAsync(updated, pin!));
    }

    [Fact]
    public async Task InvalidEditsRollBackAndOnlyGroundControlCanManageOnboardMembers()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(ground, "crew", AppRoles.Crew, "Alex", "Morgan", null)).Succeeded);
        Assert.True((await members.CreateAsync(ground, "medic", AppRoles.Medic, "Sam", "Reed", null)).Succeeded);
        var member = (await users.FindByNameAsync("crew"))!;
        Assert.False((await members.UpdateAsync(ground, member.Id, "medic", AppRoles.Medic, "Changed", "Name", null)).Succeeded);
        var unchanged = (await users.FindByIdAsync(member.Id))!;
        Assert.Equal("crew", unchanged.UserName);
        Assert.Equal("Alex", unchanged.FirstName);
        Assert.Equal(new[] { AppRoles.Crew }, await users.GetRolesAsync(unchanged));
        Assert.False((await members.UpdateAsync(ground, member.Id, "crew", AppRoles.Crew, " ", "Morgan", null)).Succeeded);
        Assert.False((await members.UpdateAsync(ground, member.Id, "crew", AppRoles.GroundControl, "Alex", "Morgan", null)).Succeeded);
        foreach (var actor in new[] { Principal(member.Id, AppRoles.Crew), Principal("medic", AppRoles.Medic), new ClaimsPrincipal() })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.UpdateAsync(actor, member.Id, "crew", AppRoles.Crew, "Alex", "Morgan", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.DeleteAsync(actor, member.Id));
        }
        var groundAccount = new ApplicationUser { UserName = "control", IsGroundControl = true };
        Assert.True((await users.CreateAsync(groundAccount, "Ground-Control!2026")).Succeeded);
        Assert.False((await members.UpdateAsync(ground, groundAccount.Id, "control", AppRoles.Crew, "Alex", "Morgan", null)).Succeeded);
        Assert.False((await members.DeleteAsync(ground, groundAccount.Id)).Succeeded);
        Assert.True((await members.DeleteAsync(ground, member.Id)).Succeeded);
        Assert.Null(await users.FindByIdAsync(member.Id));
        Assert.False((await members.DeleteAsync(ground, member.Id)).Succeeded);
        Assert.NotNull(await users.FindByNameAsync("medic"));
    }

    [Fact]
    public async Task MedicsReadAllOnboardHealthWithoutReceivingOtherPinsOrManagementRights()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var ground = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(ground, "crew", AppRoles.Crew, "Alex", "Morgan", null)).Succeeded);
        Assert.True((await members.CreateAsync(ground, "medic", AppRoles.Medic, "Sam", "Reed", null)).Succeeded);
        var crew = (await users.FindByNameAsync("crew"))!;
        var medic = (await users.FindByNameAsync("medic"))!;
        var actor = Principal(medic.Id, AppRoles.Medic);
        var clinical = new MedicMemberService(fixture.Services.GetRequiredService<ApplicationDbContext>(), fixture.Services.GetRequiredService<PinProtectionService>());
        var roster = await clinical.GetMembersAsync(actor);
        Assert.Equal(2, roster.Count);
        Assert.Contains(roster, x => x.Id == crew.Id);
        Assert.Contains(roster, x => x.Id == medic.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => clinical.GetMembersAsync(Principal(crew.Id, AppRoles.Crew)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => clinical.GetMembersAsync(new ClaimsPrincipal()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetMembersAsync(actor));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.GetPinAsync(actor, crew.Id));
        var admin = new ApplicationUser { UserName = "control", IsGroundControl = true };
        Assert.True((await users.CreateAsync(admin, "Ground-Control!2026")).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, AppRoles.Crew)).Succeeded);
        Assert.DoesNotContain(await clinical.GetMembersAsync(actor), x => x.Id == admin.Id);

        var client = new Web.Monitoring.MonitoringClient(new HttpClient(new ClinicalStub()) { BaseAddress = new("http://monitoring/") });
        var controller = new Web.Controllers.MedicController(clinical, new Web.Monitoring.CrewHealthReader(client), client)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = actor } }
        };
        var detail = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Health(crew.Id, default));
        var model = Assert.IsType<Web.Models.CrewDashboardModel>(detail.Model);
        Assert.Null(model.Pin);
        Assert.Equal("Medic", model.MemberController);
        Assert.Equal(crew.Id, model.MemberId);
        var overview = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Index(default));
        var cards = Assert.IsType<Web.Models.CrewOverviewViewModel>(overview.Model);
        Assert.True(cards.IsMedicView);
        Assert.Single(cards.Members);
        Assert.DoesNotContain(cards.Members, x => x.Id == medic.Id);
        Assert.DoesNotContain(medic.Id, cards.Health.Keys);
        Assert.All(cards.Members, x => Assert.Null(x.Pin));
        Assert.All(cards.Health.Values, x => Assert.Null(x.Pin));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Health(admin.Id, default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.HealthPhoto(admin.Id, "sample", default));
    }

    private sealed class ClinicalStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"items\":[],\"next_cursor\":null}", System.Text.Encoding.UTF8, "application/json") });
    }

    private static ClaimsPrincipal Principal(string id, string role) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)
    ], "test"));

    [Fact]
    public async Task DeletedMemberQueuesDurableCleanupAndKeepsPinReserved()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = fixture.Services.GetRequiredService<ApplicationDbContext>();
        var ground = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(ground, "cleanup-member", AppRoles.Crew, "Test", "Member", null)).Succeeded);
        var user = (await users.FindByNameAsync("cleanup-member"))!;
        var pin = (await members.GetPinAsync(ground, user.Id))!;
        Assert.True((await members.DeleteAsync(ground, user.Id)).Succeeded);
        Assert.Null(await users.FindByIdAsync(user.Id));
        var pending = await db.MonitoringPinReservations.SingleAsync();
        Assert.True(pending.DeletionPending);
        var protection = fixture.Services.GetRequiredService<PinProtectionService>();
        Assert.Equal(pin, protection.Reveal(pending.ProtectionId!, pending.ProtectedPin!));
        var replacement = new ApplicationUser { UserName = "replacement", FirstName = "New", LastName = "Member" };
        var rejected = await users.CreateAsync(replacement, pin);
        Assert.Contains(rejected.Errors, x => x.Code == "PinUnavailable");

        var handler = new DeletionStub();
        var client = new Web.Monitoring.MonitoringClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var processor = new Web.Monitoring.MonitoringDeletionProcessor(db, protection, client);
        await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(default));
        Assert.True(pending.DeletionPending);
        Assert.NotNull(pending.ProtectedPin);
        handler.Status = System.Net.HttpStatusCode.Accepted;
        await processor.ProcessAsync(default);
        Assert.False(pending.DeletionPending);
        Assert.Null(pending.ProtectedPin);
        Assert.Null(pending.ProtectionId);
        Assert.Equal(2, handler.Attempts);
        Assert.Contains((await users.CreateAsync(replacement, pin)).Errors, x => x.Code == "PinUnavailable");
    }

    private sealed class DeletionStub : HttpMessageHandler
    {
        public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.ServiceUnavailable;
        public int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Matches("^/api/v1/crew/[0-9]{6}$", request.RequestUri!.AbsolutePath);
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }

    private sealed class IdentityFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ServiceProvider Services { get; }

        public IdentityFixture()
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Identity:PinLookupKey"] = "test-only-key-with-at-least-32-bytes" }).Build());
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.User.RequireUniqueEmail = false;
            }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddSingleton<PinLookupService>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddSingleton<PinProtectionService>();
            services.AddScoped<OnboardMemberService>();
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IPasswordValidator<ApplicationUser>>(services);
            services.AddScoped<IPasswordValidator<ApplicationUser>, AccountPasswordValidator>();
            services.AddScoped<IPasswordHasher<ApplicationUser>, AccountPasswordHasher>();
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IUserValidator<ApplicationUser>>(services);
            services.AddScoped<IUserValidator<ApplicationUser>, AccountUserValidator>();
            Services = services.BuildServiceProvider();
            Services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        }

        public void Dispose()
        {
            Services.Dispose();
            connection.Dispose();
        }
    }
}
