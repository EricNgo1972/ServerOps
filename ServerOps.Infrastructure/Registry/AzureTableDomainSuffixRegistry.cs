using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.Registry;

public sealed class AzureTableDomainSuffixRegistry : IDomainSuffixRegistry
{
    private const string TableName = "Domains";
    private readonly TableClient? _tableClient;
    private readonly string _fallbackSuffix;

    public AzureTableDomainSuffixRegistry(IOptions<DomainOptions> domainOptions)
    {
        _fallbackSuffix = domainOptions.Value.DefaultDomainSuffix?.Trim().ToLowerInvariant() ?? string.Empty;
        var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _tableClient = new TableClient(connectionString, TableName);
    }

    public async Task<IReadOnlyList<string>> GetSuffixesAsync(CancellationToken ct = default)
    {
        if (_tableClient is null)
        {
            return GetFallbackSuffixes();
        }

        var suffixes = new List<string>();

        try
        {
            await foreach (var entity in _tableClient.QueryAsync<AzureDomainSuffixEntity>(
                               filter: $"PartitionKey eq 'Domain'",
                               cancellationToken: ct))
            {
                var suffix = entity.RowKey?.Trim().ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    continue;
                }

                suffixes.Add(suffix);
            }
        }
        catch
        {
            return GetFallbackSuffixes();
        }

        var results = suffixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return results.Count == 0 ? GetFallbackSuffixes() : results;
    }

    private IReadOnlyList<string> GetFallbackSuffixes()
    {
        return string.IsNullOrWhiteSpace(_fallbackSuffix)
            ? Array.Empty<string>()
            : [_fallbackSuffix];
    }
}
