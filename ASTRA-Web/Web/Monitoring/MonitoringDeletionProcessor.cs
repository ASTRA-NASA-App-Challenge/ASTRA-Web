using Data;
using Microsoft.EntityFrameworkCore;
using Services.Identity;

namespace Web.Monitoring;

public sealed class MonitoringDeletionProcessor(ApplicationDbContext db, PinProtectionService protection, MonitoringClient client)
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var failures = new List<Exception>();
        var pending = await db.MonitoringPinReservations.Where(x => x.DeletionPending).ToListAsync(ct);
        foreach (var item in pending)
        {
            try
            {
                if (item.ProtectionId is null || item.ProtectedPin is null) throw new InvalidOperationException("Incomplete PIN deletion record.");
                var pin = protection.Reveal(item.ProtectionId, item.ProtectedPin);
                await client.DeleteAsync(pin, ct);
                item.DeletionPending = false;
                item.ProtectedPin = null;
                item.ProtectionId = null;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
            { failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException("Some monitoring cleanup remains queued.", failures);
    }
}
