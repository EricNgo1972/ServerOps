using System.Text.RegularExpressions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Networking.Parsing;

public static class LinuxPortParser
{
    public static IReadOnlyList<PortInfo> ParseLinuxSs(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<PortInfo>();
        }

        var ports = new List<PortInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
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

            int? processId = null;
            var processName = string.Empty;

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
