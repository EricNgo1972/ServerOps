using ServerOps.Application.Abstractions;

namespace ServerOps.Application.Services;

public sealed class ExposureService : IExposureService
{
    private readonly IAppTopologyService _appTopologyService;
    private readonly IEndpointRegistry _endpointRegistry;
    private readonly ICloudflaredService _cloudflaredService;
    private readonly ICloudflareDnsService _cloudflareDnsService;
    private readonly ICloudflaredConfigService _cloudflaredConfigService;

    public ExposureService(
        IAppTopologyService appTopologyService,
        IEndpointRegistry endpointRegistry,
        ICloudflaredService cloudflaredService,
        ICloudflareDnsService cloudflareDnsService,
        ICloudflaredConfigService cloudflaredConfigService)
    {
        _appTopologyService = appTopologyService;
        _endpointRegistry = endpointRegistry;
        _cloudflaredService = cloudflaredService;
        _cloudflareDnsService = cloudflareDnsService;
        _cloudflaredConfigService = cloudflaredConfigService;
    }

    public async Task ExposeAsync(string serviceName, string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        }

        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname is required.", nameof(hostname));
        }

        var topology = await _appTopologyService.GetTopologyAsync(ct);
        var service = topology.FirstOrDefault(item =>
            string.Equals(item.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            throw new InvalidOperationException($"Service '{serviceName}' was not found.");
        }

        if (service.Ports.Count == 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}' has no listening ports.");
        }

        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (!tunnelInfo.IsRunning || string.IsNullOrWhiteSpace(tunnelInfo.TunnelId))
        {
            throw new InvalidOperationException("cloudflared tunnel is not running.");
        }

        var port = service.Ports[0];
        var target = $"{tunnelInfo.TunnelId}.cfargotunnel.com";
        var normalizedHostname = hostname.Trim();

        try
        {
            await _cloudflareDnsService.EnsureCNameAsync(normalizedHostname, target, ct);
            await _cloudflaredConfigService.AddIngressAsync(normalizedHostname, port, ct);
            await _cloudflaredConfigService.ReloadAsync(ct);
            await _endpointRegistry.UpsertAsync(serviceName.Trim(), normalizedHostname, ct);
        }
        catch
        {
            await _cloudflareDnsService.DeleteAsync(normalizedHostname, ct);
            throw;
        }
    }

    public async Task UpdateAsync(string serviceName, string newHostname, CancellationToken ct = default)
    {
        await UnexposeAsync(serviceName, ct);
        await ExposeAsync(serviceName, newHostname, ct);
    }

    public async Task UnexposeAsync(string serviceName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        var mappings = await _endpointRegistry.GetMappingsAsync(ct);
        var mapping = mappings.FirstOrDefault(item =>
            string.Equals(item.ServiceName?.Trim(), serviceName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(mapping?.Hostname))
        {
            return;
        }

        await _cloudflareDnsService.DeleteAsync(mapping.Hostname, ct);
        await _cloudflaredConfigService.RemoveIngressAsync(mapping.Hostname, ct);
        await _cloudflaredConfigService.ReloadAsync(ct);
        await _endpointRegistry.RemoveAsync(serviceName.Trim(), ct);
    }
}
