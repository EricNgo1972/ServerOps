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
        var operationId = string.IsNullOrWhiteSpace(request.OperationId)
            ? Guid.NewGuid().ToString("N")
            : request.OperationId.Trim();
        var deploymentTarget = string.IsNullOrWhiteSpace(request.InstanceName)
            ? request.AppName?.Trim() ?? string.Empty
            : request.InstanceName.Trim();

        if (string.IsNullOrWhiteSpace(request.AppName))
        {
            return CreateFailureResult(operationId, "Application name is required.");
        }

        if (string.IsNullOrWhiteSpace(deploymentTarget))
        {
            return CreateFailureResult(operationId, "Instance name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AssetUrl))
        {
            return CreateFailureResult(operationId, "Asset URL is required.");
        }
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
                return CreateFailureResult(operationId, "Domain suffix is required.");
            }

            try
            {
                hostname = _domainNameBuilder.Build(deploymentTarget, request.DomainSuffix);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return CreateFailureResult(operationId, ex.Message);
            }
        }
        else
        {
            hostname = null;
        }

        var deployment = await _deploymentService.DeployAsync(deploymentTarget, request.AssetUrl, request.PortOverride, operationId, ct);
        await _operationLogger.LogAsync(operationId, "Deployment", deployment.Status.ToString(), ct);
        if (deployment.Status != DeploymentStatus.Succeeded)
        {
            return new OneClickDeployResult
            {
                OperationId = operationId,
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
                OperationId = operationId,
                Deployment = deployment,
                Exposed = false,
                Message = "Deployment succeeded, but no hostname was provided."
            };
        }

        try
        {
            await _exposureService.ExposeAsync(deploymentTarget, hostname, operationId, ct);
            await _operationLogger.LogAsync(operationId, "Exposure", "Succeeded", ct);

            return new OneClickDeployResult
            {
                OperationId = operationId,
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
                OperationId = operationId,
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

    private static OneClickDeployResult CreateFailureResult(string operationId, string message)
    {
        return new OneClickDeployResult
        {
            OperationId = operationId,
            Deployment = new DeploymentResult
            {
                OperationId = operationId,
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
