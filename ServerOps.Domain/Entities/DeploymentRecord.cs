using ServerOps.Domain.Enums;

namespace ServerOps.Domain.Entities;

public sealed record DeploymentRecord
{
    public string Id { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DeploymentStatus Status { get; init; } = DeploymentStatus.Pending;
    public DateTimeOffset Timestamp { get; init; }
}
