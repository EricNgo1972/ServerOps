using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Infrastructure.Host.Parsing;

namespace ServerOps.Infrastructure.Host;

public sealed class WindowsHostService
{
    private readonly ICommandRunner _commandRunner;

    public WindowsHostService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default)
    {
        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["query", "type=service", "state=all"]
        }, ct);

        if (!result.Succeeded)
        {
            return Array.Empty<ServiceInfo>();
        }

        return WindowsServiceParser.Parse(result.StdOut);
    }
}
