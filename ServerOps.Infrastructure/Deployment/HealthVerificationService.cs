using System.Net.Sockets;
using ServerOps.Application.Abstractions;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Deployment;

public sealed class HealthVerificationService : IHealthVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly IAppCatalogService _appCatalogService;
    private readonly IAppTopologyService _appTopologyService;

    public HealthVerificationService(
        IHttpClientFactory httpClientFactory,
        IAppCatalogService appCatalogService,
        IAppTopologyService appTopologyService)
    {
        _httpClient = httpClientFactory.CreateClient();
        _appCatalogService = appCatalogService;
        _appTopologyService = appTopologyService;
    }

    public async Task<bool> VerifyAsync(string appName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        var app = await _appCatalogService.GetApplicationAsync(appName, ct);
        if (!string.IsNullOrWhiteSpace(app?.App.HealthUrl))
        {
            try
            {
                using var response = await _httpClient.GetAsync(app.App.HealthUrl, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        var topology = await _appTopologyService.GetTopologyAsync(ct);
        var service = topology.FirstOrDefault(item =>
            string.Equals(item.ServiceName, appName, StringComparison.OrdinalIgnoreCase));

        if (service is null || service.Status != ServiceStatus.Running || service.Ports.Count == 0)
        {
            return false;
        }

        foreach (var port in service.Ports)
        {
            if (await CanConnectAsync(port, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken ct)
    {
        if (port <= 0)
        {
            return false;
        }

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync("127.0.0.1", port, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
