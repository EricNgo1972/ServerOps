namespace ServerOps.Application.DTOs;

public sealed class OneClickDeployRequest
{
    public string Repo { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
    public string? Hostname { get; set; }
    public bool AutoGenerateHostname { get; set; }
}
