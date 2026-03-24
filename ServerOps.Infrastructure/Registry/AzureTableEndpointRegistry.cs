using Azure;
using Azure.Data.Tables;
using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;

namespace ServerOps.Infrastructure.Registry;

public sealed class AzureTableEndpointRegistry : IEndpointRegistry
{
    private const string TableName = "EndpointMappings";
    private const string PartitionKey = "Endpoint";

    private readonly TableClient? _tableClient;

    public AzureTableEndpointRegistry()
    {
        var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _tableClient = new TableClient(connectionString, TableName);
    }

    public async Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default)
    {
        if (_tableClient is null)
        {
            return Array.Empty<EndpointMapping>();
        }

        var mappings = new List<EndpointMapping>();

        try
        {
            await foreach (var entity in _tableClient.QueryAsync<AzureEndpointMappingEntity>(
                               filter: $"PartitionKey eq '{PartitionKey}'",
                               cancellationToken: ct))
            {
                var serviceName = string.IsNullOrWhiteSpace(entity.ServiceName) ? entity.RowKey : entity.ServiceName;
                var hostname = entity.Hostname?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(hostname))
                {
                    continue;
                }

                mappings.Add(new EndpointMapping
                {
                    ServiceName = serviceName.Trim(),
                    Hostname = hostname
                });
            }
        }
        catch
        {
            return Array.Empty<EndpointMapping>();
        }

        return mappings
            .OrderBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UpsertAsync(string serviceName, string hostname, CancellationToken ct = default)
    {
        if (_tableClient is null || string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        await _tableClient.CreateIfNotExistsAsync(ct);

        var normalizedServiceName = serviceName.Trim();
        await _tableClient.UpsertEntityAsync(new AzureEndpointMappingEntity
        {
            PartitionKey = PartitionKey,
            RowKey = NormalizeKey(normalizedServiceName),
            ServiceName = normalizedServiceName,
            Hostname = hostname.Trim()
        }, TableUpdateMode.Replace, ct);
    }

    public async Task RemoveAsync(string serviceName, CancellationToken ct = default)
    {
        if (_tableClient is null || string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        try
        {
            await _tableClient.DeleteEntityAsync(
                PartitionKey,
                NormalizeKey(serviceName.Trim()),
                ETag.All,
                ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private static string NormalizeKey(string serviceName)
    {
        return serviceName
            .Replace(".service", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
    }
}
