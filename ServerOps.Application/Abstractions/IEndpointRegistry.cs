using ServerOps.Application.Models;

namespace ServerOps.Application.Abstractions;

public interface IEndpointRegistry
{
    Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default);
    Task UpsertAsync(string serviceName, string hostname, CancellationToken ct = default);
    Task RemoveAsync(string serviceName, CancellationToken ct = default);
}
