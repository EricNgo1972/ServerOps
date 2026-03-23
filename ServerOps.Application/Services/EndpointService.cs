using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Models;

namespace ServerOps.Application.Services;

public sealed class EndpointService : IEndpointService
{
    private readonly IAppTopologyService _appTopologyService;
    private readonly IEndpointRegistry _endpointRegistry;
    private readonly ICloudflaredService _cloudflaredService;

    public EndpointService(
        IAppTopologyService appTopologyService,
        IEndpointRegistry endpointRegistry,
        ICloudflaredService cloudflaredService)
    {
        _appTopologyService = appTopologyService;
        _endpointRegistry = endpointRegistry;
        _cloudflaredService = cloudflaredService;
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> GetEndpointsAsync(CancellationToken ct = default)
    {
        var topology = await _appTopologyService.GetTopologyAsync(ct);
        var mappings = await _endpointRegistry.GetMappingsAsync(ct);
        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);

        var mappingByService = mappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.ServiceName))
            .ToDictionary(mapping => mapping.ServiceName, StringComparer.OrdinalIgnoreCase);

        return topology
            .Select(service =>
            {
                mappingByService.TryGetValue(service.ServiceName, out var mapping);
                var port = service.Ports.FirstOrDefault();
                var hostname = string.IsNullOrWhiteSpace(mapping?.Hostname) ? null : mapping.Hostname;

                return new ServiceEndpoint
                {
                    ServiceName = service.ServiceName,
                    Port = port,
                    Hostname = hostname,
                    PublicUrl = hostname is null ? null : $"https://{hostname}",
                    IsExposed = hostname is not null && tunnelInfo.IsRunning
                };
            })
            .OrderBy(endpoint => endpoint.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
