using ServerOps.Domain.Enums;

namespace ServerOps.Domain.Entities;

public sealed record ServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public ServiceStatus Status { get; init; } = ServiceStatus.Unknown;
    public int? ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
}
