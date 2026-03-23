using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Infrastructure.Networking.Parsing;

namespace ServerOps.Infrastructure.Networking;

public sealed class LinuxPortService
{
    private readonly ICommandRunner _commandRunner;

    public LinuxPortService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<IReadOnlyList<PortInfo>> GetListeningPortsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "ss",
            Arguments = ["-ltnp"]
        }, cancellationToken);

        if (!result.Succeeded)
        {
            return Array.Empty<PortInfo>();
        }

        return LinuxPortParser.ParseLinuxSs(result.StdOut);
    }
}
