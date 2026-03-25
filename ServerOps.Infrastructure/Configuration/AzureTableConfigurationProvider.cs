using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace ServerOps.Infrastructure.Configuration;

public sealed class AzureTableConfigurationProvider : ConfigurationProvider
{
    private const string TableName = "Configuration";
    private const string PartitionKey = "Config";

    public override void Load()
    {
        var connectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var tableClient = new TableClient(connectionString, TableName);
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in tableClient.Query<AzureTableConfigurationEntity>($"PartitionKey eq '{PartitionKey}'"))
            {
                var key = entity.RowKey?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                data[key] = entity.Value ?? string.Empty;
            }

            Data = data;
        }
        catch
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
