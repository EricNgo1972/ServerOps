using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface IGitHubService
{
    Task<GitHubReleaseQueryResult> GetReleasesAsync(string repo, CancellationToken cancellationToken = default);
}
