using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Application.Services;

public sealed class OneClickDeployService : IOneClickDeployService
{
    private readonly IDeploymentService _deploymentService;
    private readonly IExposureService _exposureService;
    private readonly IDomainNameBuilder _domainNameBuilder;

    public OneClickDeployService(
        IDeploymentService deploymentService,
        IExposureService exposureService,
        IDomainNameBuilder domainNameBuilder)
    {
        _deploymentService = deploymentService;
        _exposureService = exposureService;
        _domainNameBuilder = domainNameBuilder;
    }

    public async Task<OneClickDeployResult> DeployAsync(OneClickDeployRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AppName))
        {
            return CreateFailureResult("Application name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AssetUrl))
        {
            return CreateFailureResult("Asset URL is required.");
        }

        var hostname = !string.IsNullOrWhiteSpace(request.Hostname)
            ? request.Hostname.Trim()
            : request.AutoGenerateHostname
                ? _domainNameBuilder.Build(request.AppName)
                : null;

        var deployment = await _deploymentService.DeployAsync(request.AppName, request.AssetUrl, ct);
        if (deployment.Status != DeploymentStatus.Succeeded)
        {
            return new OneClickDeployResult
            {
                Deployment = deployment,
                Hostname = hostname,
                PublicUrl = string.IsNullOrWhiteSpace(hostname) ? null : $"https://{hostname}",
                Exposed = false,
                Message = deployment.Message
            };
        }

        if (string.IsNullOrWhiteSpace(hostname))
        {
            return new OneClickDeployResult
            {
                Deployment = deployment,
                Exposed = false,
                Message = "Deployment succeeded, but no hostname was provided."
            };
        }

        try
        {
            await _exposureService.ExposeAsync(request.AppName, hostname, ct);

            return new OneClickDeployResult
            {
                Deployment = deployment,
                Hostname = hostname,
                PublicUrl = $"https://{hostname}",
                Exposed = true,
                Message = "Deployment and exposure completed successfully."
            };
        }
        catch (Exception ex)
        {
            return new OneClickDeployResult
            {
                Deployment = deployment,
                Hostname = hostname,
                PublicUrl = $"https://{hostname}",
                Exposed = false,
                Message = $"Deployment succeeded, but exposure failed: {ex.Message}"
            };
        }
    }

    private static OneClickDeployResult CreateFailureResult(string message)
    {
        return new OneClickDeployResult
        {
            Deployment = new DeploymentResult
            {
                Status = DeploymentStatus.Failed,
                Stage = DeploymentStage.Failed,
                Message = message,
                StartedAtUtc = DateTimeOffset.UtcNow,
                FinishedAtUtc = DateTimeOffset.UtcNow
            },
            Exposed = false,
            Message = message
        };
    }
}
