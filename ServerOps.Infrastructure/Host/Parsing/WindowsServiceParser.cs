using System.Text.RegularExpressions;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host.Parsing;

public static class WindowsServiceParser
{
    public static IReadOnlyList<ServiceInfo> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<ServiceInfo>();
        }

        var blocks = Regex.Split(output.Trim(), @"\r?\n\r?\n");
        var services = new List<ServiceInfo>();

        foreach (var block in blocks)
        {
            if (!block.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? serviceName = null;
            ServiceStatus status = ServiceStatus.Unknown;

            foreach (var rawLine in block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (rawLine.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                {
                    serviceName = rawLine["SERVICE_NAME:".Length..].Trim();
                }
                else if (rawLine.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    status = ParseStatus(rawLine);
                }
            }

            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                services.Add(new ServiceInfo
                {
                    Name = serviceName,
                    Status = status,
                    ProcessId = null,
                    ExecutablePath = null
                });
            }
        }

        return services;
    }

    public static ServiceStatus ParseStatus(string stateLine)
    {
        if (string.IsNullOrWhiteSpace(stateLine))
        {
            return ServiceStatus.Unknown;
        }

        var normalized = stateLine.Trim().ToUpperInvariant();

        if (normalized.Contains("RUNNING", StringComparison.Ordinal))
        {
            return ServiceStatus.Running;
        }

        if (normalized.Contains("STOPPED", StringComparison.Ordinal))
        {
            return ServiceStatus.Stopped;
        }

        if (normalized.Contains("FAILED", StringComparison.Ordinal) ||
            normalized.Contains("FAILURE", StringComparison.Ordinal))
        {
            return ServiceStatus.Failed;
        }

        return ServiceStatus.Unknown;
    }
}
