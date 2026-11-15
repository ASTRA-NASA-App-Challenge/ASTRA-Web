using Common.Identity;
using Data;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Services.Identity;

var builder = WebApplication.CreateBuilder(args);
DotEnvConfiguration.Add(builder.Configuration, builder.Environment.ContentRootPath, args);

// Add services to the container.
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var sqliteConnection = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(sqliteConnection.DataSource) && sqliteConnection.DataSource != ":memory:")
{
    sqliteConnection.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqliteConnection.DataSource);
}
var connectionString = sqliteConnection.ToString();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddScoped<Services.Personnel.IOnboardPersonnelService, Services.Personnel.OnboardPersonnelService>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.AllowedUserNameCharacters += " ";
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
// Recheck changed roles and deleted accounts on their next authenticated request.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddSingleton<PinLookupService>();
builder.Services.AddDataProtection().SetApplicationName("ASTRA");
builder.Services.AddSingleton<PinProtectionService>();
builder.Services.AddScoped<OnboardMemberService>();
builder.Services.RemoveAll<IPasswordValidator<ApplicationUser>>();
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, AccountPasswordValidator>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, AccountPasswordHasher>();
builder.Services.RemoveAll<IUserValidator<ApplicationUser>>();
builder.Services.AddScoped<IUserValidator<ApplicationUser>, AccountUserValidator>();
builder.Services.AddAuthorization(options => options.AddPolicy(MedicalPolicies.ReadRecord,
    policy => policy.RequireAuthenticatedUser().AddRequirements(new ReadMedicalRecordRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, MedicalRecordAuthorizationHandler>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// Apply schema changes and provision configured mission accounts.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await IdentityDatabaseInitializer.InitializeAsync(dbContext);
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);

    // Temporary account for local development only. Never reset an existing password.
    if (app.Environment.IsDevelopment())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string username = "ASTRA Ground Control";
        var existing = await users.FindByNameAsync(username);
        if (existing is null)
        {
            var groundControl = new ApplicationUser
            {
                UserName = username,
                IsGroundControl = true
            };
            var created = await users.CreateAsync(groundControl, "Astra123!2026");
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
            var assigned = await users.AddToRoleAsync(groundControl, AppRoles.GroundControl);
            if (!assigned.Succeeded)
            {
                await users.DeleteAsync(groundControl);
                throw new InvalidOperationException(string.Join("; ", assigned.Errors.Select(error => error.Description)));
            }
        }
        else if (!existing.IsGroundControl || !await users.IsInRoleAsync(existing, AppRoles.GroundControl))
        {
            throw new InvalidOperationException("The local ground-control username is already used by another account.");
        }

        const string astronautUsername = "ASTRA Test Astronaut";
        var astronaut = await users.FindByNameAsync(astronautUsername);
        if (astronaut is null)
        {
            astronaut = new ApplicationUser
            {
                UserName = astronautUsername,
                FirstName = "Berk",
                MiddleName = "Boyanov",
                LastName = "Manolov",
                IsGroundControl = false
            };
            var created = await users.CreateAsync(astronaut, "123456");
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
            var assigned = await users.AddToRoleAsync(astronaut, AppRoles.Crew);
            if (!assigned.Succeeded)
            {
                await users.DeleteAsync(astronaut);
                throw new InvalidOperationException(string.Join("; ", assigned.Errors.Select(error => error.Description)));
            }
        }
        else if (astronaut.IsGroundControl || !await users.IsInRoleAsync(astronaut, AppRoles.Crew))
        {
            throw new InvalidOperationException("The local astronaut username is already used by another account.");
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();

