using ServerOps.Domain.Enums;

namespace ServerOps.Application.DTOs;

public sealed class DeploymentResult
{
    public string DeploymentId { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DeploymentStatus Status { get; init; } = DeploymentStatus.Pending;
    public DeploymentStage Stage { get; init; } = DeploymentStage.Pending;
    public string? Message { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
}
