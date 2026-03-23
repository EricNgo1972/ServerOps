using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IPortService
{
    Task<IReadOnlyList<PortInfo>> GetListeningPortsAsync(CancellationToken cancellationToken = default);
}
