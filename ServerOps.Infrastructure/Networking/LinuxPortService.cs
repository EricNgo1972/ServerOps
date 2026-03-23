using System.Text.RegularExpressions;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

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
            Arguments = ["-ltnp"],
            Allowed = true
        }, cancellationToken);

        if (!result.Succeeded)
        {
            return [];
        }

        var ports = new List<PortInfo>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 5)
            {
                continue;
            }

            var localAddress = parts[3];
            var portSegment = localAddress[(localAddress.LastIndexOf(':') + 1)..];
            if (!int.TryParse(portSegment, out var port))
            {
                continue;
            }

            var processName = string.Empty;
            int? processId = null;

            var pidMatch = Regex.Match(line, @"pid=(\d+)");
            if (pidMatch.Success && int.TryParse(pidMatch.Groups[1].Value, out var parsedPid))
            {
                processId = parsedPid;
            }

            var processMatch = Regex.Match(line, "\"([^\"]+)\"");
            if (processMatch.Success)
            {
                processName = processMatch.Groups[1].Value;
            }

            ports.Add(new PortInfo
            {
                Port = port,
                ProcessId = processId,
                ProcessName = processName
            });
        }

        return ports;
    }
}
