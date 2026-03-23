using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Web.Api;
using Xunit;

namespace ServerOps.Web.Tests;

public sealed class DeployApiEndpointTests
{
    [Fact]
    public async Task HandleAsync_Invalid_Api_Key_Returns_401()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-KEY"] = "wrong";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeploymentApiKey"] = "expected" })
            .Build();

        var result = await DeployApiEndpoint.HandleAsync(
            context.Request,
            new ManualDeployApiRequest { AppName = "phoebus", AssetUrl = "https://example.com/app.zip" },
            new FakeOneClickDeployService(),
            configuration,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task HandleAsync_Valid_Api_Key_Calls_OneClick_Service()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-KEY"] = "expected";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeploymentApiKey"] = "expected" })
            .Build();
        var service = new FakeOneClickDeployService();

        var result = await DeployApiEndpoint.HandleAsync(
            context.Request,
            new ManualDeployApiRequest
            {
                AppName = "phoebus",
                AssetUrl = "https://example.com/app.zip",
                Hostname = "phoebus.apps.local"
            },
            service,
            configuration,
            CancellationToken.None);

        Assert.IsAssignableFrom<IResult>(result);
        Assert.Equal(1, service.Calls);
        Assert.Equal("phoebus", service.Request?.AppName);
        Assert.Equal("phoebus.apps.local", service.Request?.Hostname);
    }

    private sealed class FakeOneClickDeployService : IOneClickDeployService
    {
        public int Calls { get; private set; }
        public OneClickDeployRequest? Request { get; private set; }

        public Task<OneClickDeployResult> DeployAsync(OneClickDeployRequest request, CancellationToken ct = default)
        {
            Calls++;
            Request = request;
            return Task.FromResult(new OneClickDeployResult
            {
                Deployment = new DeploymentResult
                {
                    AppName = request.AppName,
                    Status = DeploymentStatus.Succeeded,
                    Stage = DeploymentStage.Completed,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                },
                Hostname = request.Hostname,
                PublicUrl = string.IsNullOrWhiteSpace(request.Hostname) ? null : $"https://{request.Hostname}",
                Exposed = true
            });
        }
    }
}
