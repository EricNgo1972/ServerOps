using System.Text.RegularExpressions;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host.Parsing;

public static class LinuxServiceParser
{
    public static IReadOnlyList<ServiceInfo> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<ServiceInfo>();
        }

        var services = new List<ServiceInfo>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = Regex.Split(rawLine.Trim(), @"\s+");
            if (parts.Length < 4)
            {
                continue;
            }

            var unitName = parts[0];
            if (!unitName.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            services.Add(new ServiceInfo
            {
                Name = unitName,
                Status = ParseStatus(parts[2], parts[3]),
                ProcessId = null,
                ExecutablePath = null
            });
        }

        return services;
    }

    public static ServiceStatus ParseStatus(string activeState, string subState)
    {
        var normalizedActiveState = activeState.Trim().ToLowerInvariant();
        var normalizedSubState = subState.Trim().ToLowerInvariant();

        if (normalizedActiveState == "active" && normalizedSubState == "running")
        {
            return ServiceStatus.Running;
        }

        if (normalizedActiveState == "inactive" && normalizedSubState == "dead")
        {
            return ServiceStatus.Stopped;
        }

        if (normalizedActiveState == "failed" || normalizedSubState == "failed")
        {
            return ServiceStatus.Failed;
        }

        return ServiceStatus.Unknown;
    }

    public static int? ParseMainPid(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("MainPID=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line["MainPID=".Length..].Trim();
            if (int.TryParse(value, out var pid) && pid > 0)
            {
                return pid;
            }

            return null;
        }

        return null;
    }
}
