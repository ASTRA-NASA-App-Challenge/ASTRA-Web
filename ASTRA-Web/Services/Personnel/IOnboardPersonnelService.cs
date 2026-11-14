using Common.Models;

namespace Services.Personnel;

public interface IOnboardPersonnelService
{
    Task<OnboardPersonnelCounts> GetCountsAsync(CancellationToken cancellationToken = default);
}
