using ServerOps.Domain.Entities;

namespace ServerOps.Application.DTOs;

public sealed class AppDetailsDto
{
    public AppInstance App { get; init; } = new();
    public IReadOnlyList<PortInfo> Ports { get; init; } = [];
    public TunnelInfo Tunnel { get; init; } = new();
}
