using System.Net.Sockets;
using System.Net.NetworkInformation;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Networking;

public sealed class ConnectivityService : IConnectivityService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ConnectivityService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ConnectivitySnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var internetTask = CheckInternetConnectivityAsync(ct);

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsIncludedInterface)
            .Select(MapInterface)
            .Where(info => info.IPv4Addresses.Count > 0 || info.IPv6Addresses.Count > 0)
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var internetStatus = await internetTask;

        return new ConnectivitySnapshot
        {
            InternetStatus = internetStatus ? "Connected" : "Unavailable",
            Interfaces = interfaces
        };
    }

    private static bool IsIncludedInterface(NetworkInterface networkInterface)
    {
        if (networkInterface.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        var name = networkInterface.Name ?? string.Empty;
        var description = networkInterface.Description ?? string.Empty;
        var isTailscale = name.Contains("tailscale", StringComparison.OrdinalIgnoreCase) ||
                          description.Contains("tailscale", StringComparison.OrdinalIgnoreCase);

        if (isTailscale)
        {
            return true;
        }

        return networkInterface.NetworkInterfaceType is
            NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.Ethernet3Megabit or
            NetworkInterfaceType.FastEthernetFx or
            NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.Wireless80211;
    }

    private static NetworkInterfaceInfo MapInterface(NetworkInterface networkInterface)
    {
        var properties = networkInterface.GetIPProperties();
        var unicastAddresses = properties.UnicastAddresses;

        var ipv4Addresses = unicastAddresses
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ipv6Addresses = unicastAddresses
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
            .Select(address => address.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NetworkInterfaceInfo
        {
            Name = networkInterface.Name,
            Status = networkInterface.OperationalStatus.ToString(),
            IPv4Addresses = ipv4Addresses,
            IPv6Addresses = ipv6Addresses
        };
    }

    private async Task<bool> CheckInternetConnectivityAsync(CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            using var response = await client.GetAsync("https://www.gstatic.com/generate_204", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
