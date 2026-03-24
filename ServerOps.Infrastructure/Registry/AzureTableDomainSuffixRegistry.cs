using Azure.Data.Tables;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Registry;

public sealed class AzureTableDomainSuffixRegistry : IDomainSuffixRegistry
{
    private const string TableName = "Domains";
    private readonly TableClient? _tableClient;

    public AzureTableDomainSuffixRegistry()
    {
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
            return Array.Empty<string>();
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
            return Array.Empty<string>();
        }

        return suffixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
