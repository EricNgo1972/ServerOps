using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Models;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Services;

public sealed class AppCatalogService : IAppCatalogService
{
    private readonly IHostService _hostService;
    private readonly IPortService _portService;
    private readonly ICloudflaredService _cloudflaredService;
    private readonly ICompanyAppRegistry _companyAppRegistry;
    private readonly IManagedAppFilter _managedAppFilter;

    public AppCatalogService(
        IHostService hostService,
        IPortService portService,
        ICloudflaredService cloudflaredService,
        ICompanyAppRegistry companyAppRegistry,
        IManagedAppFilter managedAppFilter)
    {
        _hostService = hostService;
        _portService = portService;
        _cloudflaredService = cloudflaredService;
        _companyAppRegistry = companyAppRegistry;
        _managedAppFilter = managedAppFilter;
    }

    public async Task<IReadOnlyList<AppInstance>> GetApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var services = await _hostService.GetServicesAsync(cancellationToken);
        var apps = await _companyAppRegistry.GetAppsAsync(cancellationToken);
        var managedServices = await _managedAppFilter.FilterAsync(services, apps, cancellationToken);
        var ports = await _portService.GetListeningPortsAsync(cancellationToken);

        return managedServices
            .Select(service => MapToAppInstance(service, apps, ports))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AppDetailsDto?> GetApplicationAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var services = await _hostService.GetServicesAsync(cancellationToken);
        var apps = await _companyAppRegistry.GetAppsAsync(cancellationToken);
        var managedServices = await _managedAppFilter.FilterAsync(services, apps, cancellationToken);
        var service = managedServices.FirstOrDefault(x => string.Equals(x.Name, serviceName, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            return null;
        }

        var ports = await _portService.GetListeningPortsAsync(cancellationToken);
        var matchedPorts = MatchPorts(service, ports).ToList();
        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(cancellationToken);

        return new AppDetailsDto
        {
            App = MapToAppInstance(service, apps, ports),
            Ports = matchedPorts,
            Tunnel = tunnelInfo
        };
    }

    private AppInstance MapToAppInstance(ServiceInfo service, IReadOnlyList<CompanyApp> apps, IReadOnlyList<PortInfo> ports)
    {
        var app = FindApp(service.Name, apps);
        var servicePorts = MatchPorts(service, ports).Select(x => x.Port).Distinct().Order().ToList();
        var primaryPort = servicePorts.FirstOrDefault();

        return new AppInstance
        {
            Id = string.IsNullOrWhiteSpace(app?.Id) ? service.Name : app.Id,
            ServiceName = service.Name,
            DisplayName = string.IsNullOrWhiteSpace(app?.DisplayName) ? service.Name : app.DisplayName,
            IsManaged = true,
            OsType = _hostService.GetCurrentOs(),
            Status = service.Status,
            Ports = servicePorts,
            RepoUrl = app?.RepoUrl,
            HealthUrl = primaryPort > 0 ? $"http://localhost:{primaryPort}/health" : null,
            Version = "unknown",
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    private static CompanyApp? FindApp(string serviceName, IReadOnlyList<CompanyApp> apps)
    {
        var normalizedServiceName = ManagedAppFilter.Normalize(serviceName);
        if (string.IsNullOrWhiteSpace(normalizedServiceName))
        {
            return null;
        }

        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .OrderByDescending(app => ManagedAppFilter.Normalize(app.Name).Length)
            .FirstOrDefault(app =>
            {
                var normalizedAppName = ManagedAppFilter.Normalize(app.Name);
                return ManagedAppFilter.IsMatch(normalizedServiceName, normalizedAppName);
            });
    }

    private static IEnumerable<PortInfo> MatchPorts(ServiceInfo service, IReadOnlyList<PortInfo> ports)
    {
        if (service.ProcessId is int pid)
        {
            return ports.Where(port => port.ProcessId == pid);
        }

        var serviceName = service.Name;
        return ports.Where(port =>
            !string.IsNullOrWhiteSpace(port.ProcessName) &&
            (port.ProcessName.Contains(serviceName, StringComparison.OrdinalIgnoreCase) ||
             serviceName.Contains(port.ProcessName, StringComparison.OrdinalIgnoreCase)));
    }
}
