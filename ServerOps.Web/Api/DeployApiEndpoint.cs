using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Web.Api;

public static class DeployApiEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        ManualDeployApiRequest payload,
        IOneClickDeployService oneClickDeployService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var configuredKey = configuration["DeploymentApiKey"];
        var providedKey = request.Headers["X-API-KEY"].ToString();

        if (string.IsNullOrWhiteSpace(configuredKey) || !string.Equals(configuredKey, providedKey, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }

        var result = await oneClickDeployService.DeployAsync(new OneClickDeployRequest
        {
            AppName = payload.AppName,
            AssetUrl = payload.AssetUrl,
            PortOverride = payload.PortOverride,
            Hostname = payload.Hostname,
            DomainSuffix = payload.DomainSuffix,
            AutoGenerateHostname = string.IsNullOrWhiteSpace(payload.Hostname)
        }, ct);

        return TypedResults.Json(result);
    }
}
