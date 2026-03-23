using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Application.Abstractions;

public interface IHostService
{
    OsType GetCurrentOs();
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default);
}
