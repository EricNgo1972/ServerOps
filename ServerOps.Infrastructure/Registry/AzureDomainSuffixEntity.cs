using Azure;
using Azure.Data.Tables;

namespace ServerOps.Infrastructure.Registry;

internal sealed class AzureDomainSuffixEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
