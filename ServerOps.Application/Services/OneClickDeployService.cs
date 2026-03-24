using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Application.Services;

public sealed class OneClickDeployService : IOneClickDeployService
{
    private readonly IDeploymentService _deploymentService;
    private readonly IExposureService _exposureService;
    private readonly IDomainNameBuilder _domainNameBuilder;
    private readonly IOperationLogger _operationLogger;

    public OneClickDeployService(
        IDeploymentService deploymentService,
        IExposureService exposureService,
        IDomainNameBuilder domainNameBuilder,
        IOperationLogger operationLogger)
    {
        _deploymentService = deploymentService;
        _exposureService = exposureService;
        _domainNameBuilder = domainNameBuilder;
        _operationLogger = operationLogger;
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

        var operationId = Guid.NewGuid().ToString("N");
        await _operationLogger.LogAsync(operationId, "OneClick", "Started", ct);

        string? hostname;
        if (!string.IsNullOrWhiteSpace(request.Hostname))
        {
            hostname = request.Hostname.Trim();
        }
        else if (request.AutoGenerateHostname)
        {
            if (string.IsNullOrWhiteSpace(request.DomainSuffix))
            {
                return CreateFailureResult("Domain suffix is required.");
            }

            try
            {
                hostname = _domainNameBuilder.Build(request.AppName, request.DomainSuffix);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return CreateFailureResult(ex.Message);
            }
        }
        else
        {
            hostname = null;
        }

        var deployment = await _deploymentService.DeployAsync(request.AppName, request.AssetUrl, request.PortOverride, ct);
        await _operationLogger.LogAsync(operationId, "Deployment", deployment.Status.ToString(), ct);
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
            await _operationLogger.LogAsync(operationId, "Exposure", "Succeeded", ct);

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
            await _operationLogger.LogAsync(operationId, "Exposure", "Failed", ct);
            return new OneClickDeployResult
            {
                Deployment = deployment,
                Hostname = hostname,
                PublicUrl = $"https://{hostname}",
                Exposed = false,
                Message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Deployment succeeded but exposure failed"
                    : $"Deployment succeeded but exposure failed: {ex.Message}"
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
