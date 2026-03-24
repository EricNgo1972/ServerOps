using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IConnectivityService
{
    Task<ConnectivitySnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
