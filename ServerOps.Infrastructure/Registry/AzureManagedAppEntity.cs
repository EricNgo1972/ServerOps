using Azure;
using Azure.Data.Tables;

namespace ServerOps.Infrastructure.Registry;

internal sealed class AzureManagedAppEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string? AppName { get; set; }
    public string? DisplayName { get; set; }
    public string? RepoUrl { get; set; }
}
