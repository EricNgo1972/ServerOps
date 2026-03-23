using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface IAppCatalogService
{
    Task<IReadOnlyList<AppInstance>> GetApplicationsAsync(CancellationToken cancellationToken = default);
    Task<AppDetailsDto?> GetApplicationAsync(string serviceName, CancellationToken cancellationToken = default);
}
