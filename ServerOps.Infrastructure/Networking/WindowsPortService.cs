using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Infrastructure.Networking.Parsing;

namespace ServerOps.Infrastructure.Networking;

public sealed class WindowsPortService
{
    private readonly ICommandRunner _commandRunner;

    public WindowsPortService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<IReadOnlyList<PortInfo>> GetListeningPortsAsync(CancellationToken cancellationToken = default)
    {
        var portResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "netstat",
            Arguments = ["-ano", "-p", "tcp"]
        }, cancellationToken);

        var serviceResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["queryex", "state=", "all"]
        }, cancellationToken);

        if (!portResult.Succeeded)
        {
            return Array.Empty<PortInfo>();
        }

        var ports = WindowsPortParser.ParseWindowsNetstat(portResult.StdOut);
        var servicePids = serviceResult.Succeeded
            ? WindowsPortParser.ParseWindowsServicePids(serviceResult.StdOut)
            : new Dictionary<int, string>();

        return WindowsPortParser.JoinWindowsPorts(ports, servicePids);
    }
}
