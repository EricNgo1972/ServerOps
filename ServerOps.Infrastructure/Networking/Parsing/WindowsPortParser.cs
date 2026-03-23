using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Networking.Parsing;

public static class WindowsPortParser
{
    public static IReadOnlyList<PortInfo> ParseWindowsNetstat(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<PortInfo>();
        }

        var ports = new List<PortInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

            ports.Add(new PortInfo
            {
                Port = port,
                ProcessId = int.TryParse(parts[4], out var pid) ? pid : null,
                ProcessName = string.Empty
            });
        }

        return ports;
    }

    public static IReadOnlyDictionary<int, string> ParseWindowsServicePids(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new Dictionary<int, string>();
        }

        var map = new Dictionary<int, string>();
        string? serviceName = null;

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

    public static IReadOnlyList<PortInfo> JoinWindowsPorts(
        IReadOnlyList<PortInfo> ports,
        IReadOnlyDictionary<int, string> servicePids)
    {
        if (ports.Count == 0)
        {
            return Array.Empty<PortInfo>();
        }

        return ports
            .Select(port =>
            {
                var processName = string.Empty;
                if (port.ProcessId is int pid && servicePids.TryGetValue(pid, out var mappedName))
                {
                    processName = mappedName;
                }

                return new PortInfo
                {
                    Port = port.Port,
                    ProcessId = port.ProcessId,
                    ProcessName = processName
                };
            })
            .ToList();
    }
}
