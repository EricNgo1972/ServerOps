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

        var enrichedServices = new List<ServiceInfo>(services.Count);

        foreach (var service in services)
        {
            var mainPidResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["show", service.Name, "--property", "MainPID"]
            }, ct);

            var processId = mainPidResult.Succeeded
                ? LinuxServiceParser.ParseMainPid(mainPidResult.StdOut)
                : null;

            enrichedServices.Add(service with { ProcessId = processId });
        }

        return enrichedServices;
    }
}
