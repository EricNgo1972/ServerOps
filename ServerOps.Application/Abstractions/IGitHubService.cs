using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface IGitHubService
{
    Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(string repo, CancellationToken cancellationToken = default);
}
