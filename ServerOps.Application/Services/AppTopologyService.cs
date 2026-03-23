using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Application.Services;

public sealed class AppTopologyService : IAppTopologyService
{
    private readonly IHostService _hostService;
    private readonly IPortService _portService;

    public AppTopologyService(IHostService hostService, IPortService portService)
    {
        _hostService = hostService;
        _portService = portService;
    }

    public async Task<IReadOnlyList<ServiceTopology>> GetTopologyAsync(CancellationToken ct = default)
    {
        var services = await _hostService.GetServicesAsync(ct);
        var ports = await _portService.GetListeningPortsAsync(ct);

        return services
            .Select(service =>
            {
                var matchedPorts = service.ProcessId is int pid
                    ? ports.Where(port => port.ProcessId == pid).Select(port => port.Port).Distinct().Order().ToList()
                    : [];

                return new ServiceTopology
                {
                    ServiceName = service.Name,
                    Status = service.Status,
                    ProcessId = service.ProcessId,
                    Ports = matchedPorts
                };
            })
            .OrderBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
