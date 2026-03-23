using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;

namespace ServerOps.Infrastructure.Registry;

public sealed class InMemoryEndpointRegistry : IEndpointRegistry
{
    private static readonly IReadOnlyList<EndpointMapping> Mappings =
    [
        new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" },
        new EndpointMapping { ServiceName = "ocr", Hostname = "ocr.local" },
        new EndpointMapping { ServiceName = "shared-a", Hostname = "shared-a.local" }
    ];

    public Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Mappings);
    }
}
