namespace ServerOps.Application.DTOs;

public sealed class NetworkInterfaceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> IPv4Addresses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IPv6Addresses { get; init; } = Array.Empty<string>();
}
