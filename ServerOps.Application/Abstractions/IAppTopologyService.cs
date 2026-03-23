using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IAppTopologyService
{
    Task<IReadOnlyList<ServiceTopology>> GetTopologyAsync(CancellationToken ct = default);
}
