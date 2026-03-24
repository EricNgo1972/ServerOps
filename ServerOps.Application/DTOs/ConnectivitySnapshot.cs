namespace ServerOps.Application.DTOs;

public sealed class ConnectivitySnapshot
{
    public string InternetStatus { get; init; } = "Unknown";
    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; init; } = Array.Empty<NetworkInterfaceInfo>();
}
