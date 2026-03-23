using ServerOps.Domain.Enums;

namespace ServerOps.Domain.Entities;

public sealed record AppInstance
{
    public string Id { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsManaged { get; init; }
    public OsType OsType { get; init; } = OsType.Linux;
    public ServiceStatus Status { get; init; } = ServiceStatus.Unknown;
    public IReadOnlyList<int> Ports { get; init; } = Array.Empty<int>();
    public string? RepoUrl { get; init; }
    public string? HealthUrl { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}
