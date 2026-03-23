using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Services;

public sealed class ManagedAppFilter : IManagedAppFilter
{
    public Task<IReadOnlyList<ServiceInfo>> FilterAsync(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<CompanyApp> apps,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (services.Count == 0 || apps.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ServiceInfo>>(Array.Empty<ServiceInfo>());
        }

        var normalizedApps = apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .Select(app => new NormalizedApp(app, Normalize(app.Name)))
            .Where(x => !string.IsNullOrWhiteSpace(x.NormalizedName))
            .OrderByDescending(x => x.NormalizedName.Length)
            .ToList();

        if (normalizedApps.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ServiceInfo>>(Array.Empty<ServiceInfo>());
        }

        var matches = new List<ServiceInfo>();

        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Name))
            {
                continue;
            }

            var normalizedServiceName = Normalize(service.Name);
            if (string.IsNullOrWhiteSpace(normalizedServiceName))
            {
                continue;
            }

            var matchedApp = normalizedApps
                .FirstOrDefault(app => IsMatch(normalizedServiceName, app.NormalizedName));

            if (matchedApp is null)
            {
                continue;
            }

            matches.Add(service);
        }

        return Task.FromResult<IReadOnlyList<ServiceInfo>>(matches);
    }

    internal static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.EndsWith(".service", StringComparison.Ordinal))
        {
            normalized = normalized[..^".service".Length];
        }

        return normalized.Trim();
    }

    internal static bool IsMatch(string normalizedServiceName, string normalizedAppName)
    {
        if (string.IsNullOrWhiteSpace(normalizedServiceName) || string.IsNullOrWhiteSpace(normalizedAppName))
        {
            return false;
        }

        return string.Equals(normalizedServiceName, normalizedAppName, StringComparison.Ordinal) ||
               normalizedServiceName.StartsWith($"{normalizedAppName}-", StringComparison.Ordinal) ||
               normalizedServiceName.StartsWith($"{normalizedAppName}.", StringComparison.Ordinal);
    }

    private sealed record NormalizedApp(CompanyApp App, string NormalizedName);
}
