namespace ServerOps.Infrastructure.Configuration;

public sealed class CloudflareOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
}
