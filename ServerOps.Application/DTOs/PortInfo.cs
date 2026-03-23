namespace ServerOps.Application.DTOs;

public sealed class PortInfo
{
    public int Port { get; init; }
    public int? ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
}
