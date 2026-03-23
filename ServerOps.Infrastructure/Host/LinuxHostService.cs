using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Infrastructure.Host.Parsing;

namespace ServerOps.Infrastructure.Host;

public sealed class LinuxHostService
{
    private readonly ICommandRunner _commandRunner;

    public LinuxHostService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default)
    {
        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["list-units", "--type=service", "--all", "--no-legend", "--no-pager"]
        }, ct);

        if (!result.Succeeded)
        {
            return Array.Empty<ServiceInfo>();
        }

        var services = LinuxServiceParser.Parse(result.StdOut);
        if (services.Count == 0)
        {
            return services;
        }

        var pidResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["show", "--type=service", "--property=Id,MainPID"]
        }, ct);

        if (!pidResult.Succeeded)
        {
            return services;
        }

        var pidMap = LinuxServiceParser.ParseServicePidMap(pidResult.StdOut);

        return services
            .Select(service => service with
            {
                ProcessId = pidMap.TryGetValue(service.Name, out var pid) ? pid : null
            })
            .ToList();
    }
}
