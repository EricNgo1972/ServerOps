using ServerOps.Application.Models;

namespace ServerOps.Application.Abstractions;

public interface IEndpointRegistry
{
    Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default);
}
