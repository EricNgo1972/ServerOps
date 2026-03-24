using ServerOps.Application.Abstractions;

namespace ServerOps.Application.Services;

public sealed class ExposureService : IExposureService
{
    private readonly IAppTopologyService _appTopologyService;
    private readonly IEndpointRegistry _endpointRegistry;
    private readonly ICloudflaredService _cloudflaredService;
    private readonly ICloudflareDnsService _cloudflareDnsService;
    private readonly ICloudflaredConfigService _cloudflaredConfigService;
    private readonly IOperationLogger _operationLogger;

    public ExposureService(
        IAppTopologyService appTopologyService,
        IEndpointRegistry endpointRegistry,
        ICloudflaredService cloudflaredService,
        ICloudflareDnsService cloudflareDnsService,
        ICloudflaredConfigService cloudflaredConfigService,
        IOperationLogger operationLogger)
    {
        _appTopologyService = appTopologyService;
        _endpointRegistry = endpointRegistry;
        _cloudflaredService = cloudflaredService;
        _cloudflareDnsService = cloudflareDnsService;
        _cloudflaredConfigService = cloudflaredConfigService;
        _operationLogger = operationLogger;
    }

    public async Task ExposeAsync(string serviceName, string hostname, string? operationId = null, CancellationToken ct = default)
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
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();

        try
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Expose started service={serviceName}, hostname={normalizedHostname}, port={port}", ct);
            await _cloudflareDnsService.EnsureCNameAsync(normalizedHostname, target, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"DNS updated hostname={normalizedHostname}, target={target}", ct);
            await _cloudflaredConfigService.AddIngressAsync(normalizedHostname, port, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Ingress added hostname={normalizedHostname}, port={port}", ct);
            await _cloudflaredConfigService.ReloadAsync(ct);
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", "cloudflared reloaded", ct);
            await _endpointRegistry.UpsertAsync(serviceName.Trim(), normalizedHostname, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Completed service={serviceName}, hostname={normalizedHostname}", ct);
        }
        catch (Exception ex)
        {
            await _cloudflareDnsService.DeleteAsync(normalizedHostname, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Failed hostname={normalizedHostname}, error={ex.Message}", ct);
            throw;
        }
    }

    public async Task UpdateAsync(string serviceName, string newHostname, string? operationId = null, CancellationToken ct = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Update started service={serviceName}, hostname={newHostname.Trim()}", ct);
        await UnexposeAsync(serviceName, resolvedOperationId, ct);
        await ExposeAsync(serviceName, newHostname, resolvedOperationId, ct);
    }

    public async Task UnexposeAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();

        var mappings = await _endpointRegistry.GetMappingsAsync(ct);
        var mapping = mappings.FirstOrDefault(item =>
            string.Equals(item.ServiceName?.Trim(), serviceName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(mapping?.Hostname))
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Unexpose skipped service={serviceName.Trim()}, reason=no mapping", ct);
            return;
        }

        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Unexpose started service={serviceName.Trim()}, hostname={mapping.Hostname}", ct);
        await _cloudflareDnsService.DeleteAsync(mapping.Hostname, ct);
        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"DNS removed hostname={mapping.Hostname}", ct);
        await _cloudflaredConfigService.RemoveIngressAsync(mapping.Hostname, ct);
        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Ingress removed hostname={mapping.Hostname}", ct);
        await _cloudflaredConfigService.ReloadAsync(ct);
        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", "cloudflared reloaded", ct);
        await _endpointRegistry.RemoveAsync(serviceName.Trim(), ct);
        await _operationLogger.LogAsync(resolvedOperationId, "Exposure", $"Completed unexpose service={serviceName.Trim()}", ct);
    }
}
