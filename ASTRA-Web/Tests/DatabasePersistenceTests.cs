using System.Security.Cryptography;
using Data;
using Entities.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class DatabasePersistenceTests
{
    [Fact]
    public async Task RestartPreservesAccountsAndDoesNotRewriteAnUpToDateDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("astra-persistence-");
        var path = Path.Combine(directory.FullName, "astra.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        try
        {
            await using (var firstRun = new ApplicationDbContext(options))
            {
                await IdentityDatabaseInitializer.InitializeAsync(firstRun);
                firstRun.Users.Add(new ApplicationUser
                {
                    Id = "persistent-user",
                    UserName = "ground-control",
                    IsGroundControl = true,
                    PasswordHash = "existing-password-hash"
                });
                await firstRun.SaveChangesAsync();
            }
            var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
            for (var restart = 0; restart < 2; restart++)
            {
                await using var nextRun = new ApplicationDbContext(options);
                await IdentityDatabaseInitializer.InitializeAsync(nextRun);
                var user = await nextRun.Users.SingleAsync();
                Assert.Equal("persistent-user", user.Id);
                Assert.Equal("existing-password-hash", user.PasswordHash);
            }
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
