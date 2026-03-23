namespace ServerOps.Application.DTOs;

public sealed class ServiceEndpoint
{
    public string ServiceName { get; init; } = string.Empty;
    public int Port { get; init; }
    public string? Hostname { get; init; }
    public string? PublicUrl { get; init; }
    public bool IsExposed { get; init; }
}
