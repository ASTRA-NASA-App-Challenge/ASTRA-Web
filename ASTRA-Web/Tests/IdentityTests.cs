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
            firstName!, lastName!, null, "012345");
        Assert.Contains(result.Errors, item => item.Code == error);
        Assert.Empty(await fixture.Services.GetRequiredService<ApplicationDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task GroundControlSuppliedPinsAreValidatedWithoutCreatingPartialAccounts()
    {
        using var fixture = new IdentityFixture();
        await IdentitySeeder.SeedAsync(fixture.Services, new ConfigurationBuilder().Build());
        var members = fixture.Services.GetRequiredService<OnboardMemberService>();
        var actor = Principal("ground", AppRoles.GroundControl);
        Assert.True((await members.CreateAsync(actor, "crew", AppRoles.Crew, "Alex", "Morgan", null, "001234")).Succeeded);
        var users = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var crew = (await users.FindByNameAsync("crew"))!;
        Assert.True(await users.CheckPasswordAsync(crew, "001234"));
        Assert.Equal("001234", await members.GetPinAsync(actor, crew.Id));
        var duplicate = await members.CreateAsync(actor, "medic", AppRoles.Medic, "Sam", "Reed", null, "001234");
        Assert.Contains(duplicate.Errors, error => error.Code == "PinUnavailable");
        var invalid = await members.CreateAsync(actor, "invalid", AppRoles.Crew, "Taylor", "Lee", null, "12345");
        Assert.Contains(invalid.Errors, error => error.Code == "InvalidPin");
        Assert.Equal(1, await users.Users.CountAsync());
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
        Assert.Equal(6, (await db.Database.GetAppliedMigrationsAsync()).Count());
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
        Assert.True((await members.CreateAsync(ground, "crew", AppRoles.Crew, " Alex ", " Morgan ", null, "012345")).Succeeded);
        Assert.True((await members.CreateAsync(ground, "medic", AppRoles.Medic, "Sam", "Reed", " Avery ", "654321")).Succeeded);
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => members.CreateAsync(Principal(medic.Id, AppRoles.Medic), "unauthorized", AppRoles.Crew, "", "", null, "012345"));
        Assert.False((await members.CreateAsync(ground, "invalid", AppRoles.GroundControl, "", "", null, "012345")).Succeeded);
        var replacement = new[] { "012345", "012346", "012347" }.First(candidate => candidate != pin && candidate != medicPin);
        Assert.True((await users.ChangePasswordAsync(crew, pin, replacement)).Succeeded);
        Assert.Equal(replacement, await members.GetPinAsync(ground, crew.Id));
    }

    private static ClaimsPrincipal Principal(string id, string role) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)
    ], "test"));

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
