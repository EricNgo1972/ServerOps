using System.Text.RegularExpressions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Networking.Parsing;

public static class LinuxPortParser
{
    private static readonly Regex PortRegex = new(@":(\d+)", RegexOptions.Compiled);
    private static readonly Regex PidRegex = new(@"pid=(\d+)", RegexOptions.Compiled);
    private static readonly Regex ProcessNameRegex = new("\"([^\"]+)\"", RegexOptions.Compiled);

    public static IReadOnlyList<PortInfo> ParseLinuxSs(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<PortInfo>();
        }

        var ports = new List<PortInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("LISTEN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var portMatch = PortRegex.Match(line);
            if (!portMatch.Success || !int.TryParse(portMatch.Groups[1].Value, out var port))
            {
                continue;
            }

            int? processId = null;
            var processName = string.Empty;

            var pidMatch = PidRegex.Match(line);
            if (pidMatch.Success && int.TryParse(pidMatch.Groups[1].Value, out var parsedPid))
            {
                processId = parsedPid;
            }

            var processMatch = ProcessNameRegex.Match(line);
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

        return ports
            .GroupBy(x => x.Port)
            .Select(group => group.First())
            .ToList();
    }
}
