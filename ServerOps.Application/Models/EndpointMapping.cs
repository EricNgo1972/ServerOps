namespace ServerOps.Application.Models;

public sealed record EndpointMapping
{
    public string ServiceName { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
}
