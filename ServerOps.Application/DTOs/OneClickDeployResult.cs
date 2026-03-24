namespace ServerOps.Application.DTOs;

public sealed class OneClickDeployResult
{
    public string OperationId { get; init; } = string.Empty;
    public DeploymentResult Deployment { get; init; } = new();
    public string? Hostname { get; init; }
    public string? PublicUrl { get; init; }
    public bool Exposed { get; init; }
    public string? Message { get; init; }
}
