using Microsoft.EntityFrameworkCore;

namespace Web.Data;

public static class IdentityDatabaseInitializer
{
    public static async Task InitializeAsync(ApplicationDbContext db)
    {
        // Earlier versions created the standard Identity schema with EnsureCreated.
        // Adopt that schema as the initial migration without deleting existing accounts.
        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AspNetUsers'";
            var hasIdentity = Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
            if (hasIdentity && !(await db.Database.GetAppliedMigrationsAsync()).Any())
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                        "ProductVersion" TEXT NOT NULL);
                    INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ('20261114090055_InitialIdentity', '10.0.15');
                    """);
            }
            await db.Database.MigrateAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
