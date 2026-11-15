using Common.Models;
using Web.Models;

namespace Web.Monitoring;

// Call only after the controller has authorized the viewer and resolved onboard members.
public sealed class CrewHealthReader(MonitoringClient monitoring)
{
    public async Task<CrewDashboardModel> ReadAsync(OnboardMemberSummary member, int days, bool summary, CancellationToken ct)
    {
        var record = new MonitoringRecord([], []);
        string? error = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(summary ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30));
        try
        {
            if (member.Pin is null) error = "Monitoring identity unavailable. Check the member account.";
            else record = summary ? await monitoring.ReadLatestAsync(member.Pin, timeout.Token) : await monitoring.ReadWindowAsync(member.Pin, timeout.Token, days);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            ct.ThrowIfCancellationRequested();
            error = "Monitoring unavailable. Retry to load this member's readings.";
        }
        return new CrewDashboardModel { MemberId = member.Id, MemberRole = member.Role, Username = member.Username, Name = member.FullName, Pin = member.Pin, Days = days, Record = record, Error = error };
    }

    public async Task<IReadOnlyDictionary<string, CrewDashboardModel>> ReadRosterAsync(IReadOnlyList<OnboardMemberSummary> members, CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(4);
        var results = await Task.WhenAll(members.Select(async member =>
        {
            await slots.WaitAsync(ct);
            try { return new KeyValuePair<string, CrewDashboardModel>(member.Id, await ReadAsync(member, 7, true, ct)); }
            finally { slots.Release(); }
        }));
        return results.ToDictionary(x => x.Key, x => x.Value);
    }
}
