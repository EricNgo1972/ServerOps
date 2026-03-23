using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

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
            Arguments = ["-ano", "-p", "tcp"],
            Allowed = true
        }, cancellationToken);

        var serviceResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["queryex", "state=", "all"],
            Allowed = true
        }, cancellationToken);

        if (!portResult.Succeeded)
        {
            return [];
        }

        var processMap = BuildProcessMap(serviceResult.StdOut);
        var ports = new List<PortInfo>();

        foreach (var line in portResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5 || !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var endpoint = parts[1];
            var portSegment = endpoint[(endpoint.LastIndexOf(':') + 1)..];
            if (!int.TryParse(portSegment, out var port))
            {
                continue;
            }

            int? processId = int.TryParse(parts[4], out var pid) ? pid : null;
            processMap.TryGetValue(processId ?? 0, out var processName);

            ports.Add(new PortInfo
            {
                Port = port,
                ProcessId = processId,
                ProcessName = processName ?? string.Empty
            });
        }

        return ports;
    }

    private static Dictionary<int, string> BuildProcessMap(string output)
    {
        var map = new Dictionary<int, string>();
        string serviceName = string.Empty;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                serviceName = line["SERVICE_NAME:".Length..].Trim();
            }
            else if (line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(line.IndexOf(':') + 1)..].Trim();
                if (int.TryParse(value, out var pid) && pid > 0 && !string.IsNullOrWhiteSpace(serviceName))
                {
                    map[pid] = serviceName;
                }
            }
        }

        return map;
    }
}
