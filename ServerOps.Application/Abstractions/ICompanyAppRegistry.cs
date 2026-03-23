using ServerOps.Application.Models;

namespace ServerOps.Application.Abstractions;

public interface ICompanyAppRegistry
{
    Task<IReadOnlyList<CompanyApp>> GetAppsAsync(CancellationToken ct = default);
}
