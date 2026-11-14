using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddSingleton<PinLookupService>();
builder.Services.AddDataProtection().SetApplicationName("ASTRA");
builder.Services.AddSingleton<PinProtectionService>();
builder.Services.AddScoped<OnboardMemberService>();
builder.Services.RemoveAll<IPasswordValidator<ApplicationUser>>();
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, AccountPasswordValidator>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, AccountPasswordHasher>();
builder.Services.AddAuthorization(options => options.AddPolicy(MedicalPolicies.ReadRecord,
    policy => policy.RequireAuthenticatedUser().AddRequirements(new ReadMedicalRecordRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, MedicalRecordAuthorizationHandler>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(options =>
{
    // Remove the registration routes supplied by the Identity UI library too.
    options.Conventions.AddAreaPageRouteModelConvention("Identity", "/Account/Register", model => model.Selectors.Clear());
    options.Conventions.AddAreaPageRouteModelConvention("Identity", "/Account/RegisterConfirmation", model => model.Selectors.Clear());
});

var app = builder.Build();

// Apply schema changes and provision configured mission accounts.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await IdentityDatabaseInitializer.InitializeAsync(dbContext);
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);
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
