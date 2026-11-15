using Data;
using Microsoft.EntityFrameworkCore;
using Services.Identity;

namespace Web.Monitoring;

// Identity SQLite is the durable source for retries, including members created before integration.
public sealed class PinRegistrationWorker(IServiceScopeFactory scopes, ILogger<PinRegistrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var protection = scope.ServiceProvider.GetRequiredService<PinProtectionService>();
                var client = scope.ServiceProvider.GetRequiredService<MonitoringClient>();
                try { await scope.ServiceProvider.GetRequiredService<MonitoringDeletionProcessor>().ProcessAsync(stoppingToken); }
                catch (AggregateException) { logger.LogWarning("Some monitoring cleanup is still queued; other PINs will continue syncing."); }
                var members = await db.Users.AsNoTracking().Where(x => !x.IsGroundControl && x.ProtectedPin != null).ToListAsync(stoppingToken);
                foreach (var member in members)
                {
                    try { await client.RegisterAsync(protection.Reveal(member.Id, member.ProtectedPin!), stoppingToken); }
                    catch (HttpRequestException) { logger.LogWarning("A PIN registration failed; retrying on the next pass."); }
                    catch (System.Security.Cryptography.CryptographicException) { logger.LogWarning("A member PIN could not be decrypted for monitoring registration."); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
            { logger.LogWarning("Monitoring registration is unavailable; it will be retried."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
