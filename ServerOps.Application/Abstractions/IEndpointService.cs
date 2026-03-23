using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IEndpointService
{
    Task<IReadOnlyList<ServiceEndpoint>> GetEndpointsAsync(CancellationToken ct = default);
}
