using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using ServerOps.Application.Abstractions;
using ServerOps.Application.Models;

namespace ServerOps.Infrastructure.Registry;

public sealed class AzureTableAppRegistry
{
    private const string TableName = "ManagedApps";
    private readonly TableClient? _tableClient;

    public AzureTableAppRegistry(IConfiguration configuration)
    {
        var connectionString = configuration["STORAGE_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _tableClient = new TableClient(connectionString, TableName);
    }

    public async Task<IReadOnlyList<CompanyApp>> GetAppsAsync(CancellationToken ct = default)
    {
        if (_tableClient is null)
        {
            return Array.Empty<CompanyApp>();
        }

        var apps = new List<CompanyApp>();

        try
        {
            await foreach (var entity in _tableClient.QueryAsync<AzureManagedAppEntity>(
                               filter: $"PartitionKey eq 'App'",
                               cancellationToken: ct))
            {
                var appName = string.IsNullOrWhiteSpace(entity.AppName) ? entity.RowKey : entity.AppName;
                if (string.IsNullOrWhiteSpace(appName))
                {
                    continue;
                }

                apps.Add(new CompanyApp
                {
                    Id = entity.RowKey,
                    Name = appName,
                    DisplayName = entity.DisplayName,
                    RepoUrl = entity.RepoUrl
                });
            }
        }
        catch
        {
            return Array.Empty<CompanyApp>();
        }

        return apps;
    }
}
