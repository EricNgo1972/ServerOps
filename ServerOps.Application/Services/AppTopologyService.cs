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
        var portsByProcessId = ports
            .Where(port => port.ProcessId.HasValue)
            .GroupBy(port => port.ProcessId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(port => port.Port).Distinct().Order().ToList());

        return services
            .Where(service => service.Status == Domain.Enums.ServiceStatus.Running)
            .Select(service =>
            {
                var matchedPorts = service.ProcessId is int pid
                    && portsByProcessId.TryGetValue(pid, out var servicePorts)
                        ? servicePorts
                        : Array.Empty<int>();

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
