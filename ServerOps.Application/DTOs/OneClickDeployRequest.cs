namespace ServerOps.Application.DTOs;

public sealed class OneClickDeployRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
    public int? PortOverride { get; set; }
    public string? Hostname { get; set; }
    public string? DomainSuffix { get; set; }
    public bool AutoGenerateHostname { get; set; }
}
