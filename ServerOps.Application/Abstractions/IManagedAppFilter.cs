using ServerOps.Application.Models;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface IManagedAppFilter
{
    Task<IReadOnlyList<ServiceInfo>> FilterAsync(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<CompanyApp> apps,
        CancellationToken ct = default);
}
