using ServerOps.Domain.Entities;

namespace ServerOps.Application.DTOs;

public sealed class GitHubReleaseQueryResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<ReleaseInfo> Releases { get; init; } = Array.Empty<ReleaseInfo>();
}
