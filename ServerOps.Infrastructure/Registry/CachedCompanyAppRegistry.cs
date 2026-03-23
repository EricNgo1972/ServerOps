using Microsoft.Extensions.Caching.Memory;
using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;

namespace ServerOps.Infrastructure.Registry;

public sealed class CachedCompanyAppRegistry : ICompanyAppRegistry
{
    private const string CacheKey = "company-app-registry";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly AzureTableAppRegistry _innerRegistry;
    private readonly IMemoryCache _memoryCache;

    public CachedCompanyAppRegistry(AzureTableAppRegistry innerRegistry, IMemoryCache memoryCache)
    {
        _innerRegistry = innerRegistry;
        _memoryCache = memoryCache;
    }

    public Task<IReadOnlyList<CompanyApp>> GetAppsAsync(CancellationToken ct = default)
    {
        return GetOrCreateAsync(ct);
    }

    private async Task<IReadOnlyList<CompanyApp>> GetOrCreateAsync(CancellationToken ct)
    {
        var cachedApps = await _memoryCache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _innerRegistry.GetAppsAsync(ct);
        });

        return cachedApps ?? Array.Empty<CompanyApp>();
    }
}
