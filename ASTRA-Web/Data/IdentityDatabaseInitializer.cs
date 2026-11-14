using Microsoft.EntityFrameworkCore;

namespace Data;

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
            // MigrateAsync acquires a SQLite migration lock even when no schema changes
            // are pending. Avoid those writes on ordinary restarts.
            if ((await db.Database.GetPendingMigrationsAsync()).Any())
                await db.Database.MigrateAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
