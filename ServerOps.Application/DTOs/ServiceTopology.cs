using ServerOps.Domain.Enums;

namespace ServerOps.Application.DTOs;

public sealed class ServiceTopology
{
    public string ServiceName { get; init; } = string.Empty;
    public ServiceStatus Status { get; init; } = ServiceStatus.Unknown;
    public int? ProcessId { get; init; }
    public IReadOnlyList<int> Ports { get; init; } = Array.Empty<int>();
}
