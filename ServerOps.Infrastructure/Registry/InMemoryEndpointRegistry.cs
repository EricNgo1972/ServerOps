using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;

namespace ServerOps.Infrastructure.Registry;

public sealed class InMemoryEndpointRegistry : IEndpointRegistry
{
    private readonly Dictionary<string, EndpointMapping> _mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["phoebus-api"] = new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" },
        ["ocr"] = new EndpointMapping { ServiceName = "ocr", Hostname = "ocr.local" },
        ["shared-a"] = new EndpointMapping { ServiceName = "shared-a", Hostname = "shared-a.local" }
    };

    public Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<EndpointMapping> snapshot = _mappings.Values
            .OrderBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task UpsertAsync(string serviceName, string hostname, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(hostname))
        {
            return Task.CompletedTask;
        }

        _mappings[serviceName.Trim()] = new EndpointMapping
        {
            ServiceName = serviceName.Trim(),
            Hostname = hostname.Trim()
        };

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string serviceName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Task.CompletedTask;
        }

        _mappings.Remove(serviceName.Trim());
        return Task.CompletedTask;
    }
}
