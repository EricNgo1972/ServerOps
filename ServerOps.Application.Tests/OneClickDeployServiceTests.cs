using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Services;
using ServerOps.Domain.Enums;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class OneClickDeployServiceTests
{
    [Fact]
    public async Task DeployAsync_Deployment_Fails_Exposure_Is_Not_Called()
    {
        var exposure = new FakeExposureService();
        var service = new OneClickDeployService(
            new FakeDeploymentService(new DeploymentResult
            {
                Status = DeploymentStatus.Failed,
                Stage = DeploymentStage.Failed,
                StartedAtUtc = DateTimeOffset.UtcNow,
                FinishedAtUtc = DateTimeOffset.UtcNow
            }),
            exposure,
            new FakeDomainNameBuilder("phoebus.apps.local"),
            new FakeOperationLogger());

        var result = await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            DomainSuffix = "apps.local",
            AutoGenerateHostname = true
        });

        Assert.False(result.Exposed);
        Assert.Equal(0, exposure.Calls);
    }

    [Fact]
    public async Task DeployAsync_Deployment_Succeeds_Exposure_Succeeds_Returns_Url()
    {
        var exposure = new FakeExposureService();
        var service = new OneClickDeployService(
            new FakeDeploymentService(SucceededDeployment()),
            exposure,
            new FakeDomainNameBuilder("phoebus.apps.local"),
            new FakeOperationLogger());

        var result = await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            Hostname = "phoebus.apps.local"
        });

        Assert.True(result.Exposed);
        Assert.Equal("https://phoebus.apps.local", result.PublicUrl);
        Assert.Equal("phoebus.apps.local", exposure.Hostname);
    }

    [Fact]
    public async Task DeployAsync_Deployment_Succeeds_Exposure_Fails_Deployment_Remains_Succeeded()
    {
        var service = new OneClickDeployService(
            new FakeDeploymentService(SucceededDeployment()),
            new FakeExposureService(throwOnExpose: true),
            new FakeDomainNameBuilder("phoebus.apps.local"),
            new FakeOperationLogger());

        var result = await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            Hostname = "phoebus.apps.local"
        });

        Assert.Equal(DeploymentStatus.Succeeded, result.Deployment.Status);
        Assert.False(result.Exposed);
    }

    [Fact]
    public async Task DeployAsync_Manual_Hostname_Is_Used()
    {
        var exposure = new FakeExposureService();
        var service = new OneClickDeployService(
            new FakeDeploymentService(SucceededDeployment()),
            exposure,
            new FakeDomainNameBuilder("generated.apps.local"),
            new FakeOperationLogger());

        var result = await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            Hostname = "manual.apps.local"
        });

        Assert.Equal("manual.apps.local", result.Hostname);
        Assert.Equal("manual.apps.local", exposure.Hostname);
    }

    [Fact]
    public async Task DeployAsync_Auto_Generated_Hostname_Is_Used_When_Hostname_Missing()
    {
        var exposure = new FakeExposureService();
        var service = new OneClickDeployService(
            new FakeDeploymentService(SucceededDeployment()),
            exposure,
            new FakeDomainNameBuilder("phoebus.apps.local"),
            new FakeOperationLogger());

        var result = await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            DomainSuffix = "apps.local",
            AutoGenerateHostname = true
        });

        Assert.Equal("phoebus.apps.local", result.Hostname);
        Assert.Equal("phoebus.apps.local", exposure.Hostname);
    }

    [Fact]
    public async Task DeployAsync_Passes_Port_Override_To_Deployment()
    {
        var deployment = new FakeDeploymentService(SucceededDeployment());
        var service = new OneClickDeployService(
            deployment,
            new FakeExposureService(),
            new FakeDomainNameBuilder("phoebus.apps.local"),
            new FakeOperationLogger());

        await service.DeployAsync(new OneClickDeployRequest
        {
            AppName = "phoebus",
            AssetUrl = "https://example.com/app.zip",
            PortOverride = 5200
        });

        Assert.Equal(5200, deployment.PortOverride);
    }

    private static DeploymentResult SucceededDeployment()
    {
        return new DeploymentResult
        {
            AppName = "phoebus",
            Status = DeploymentStatus.Succeeded,
            Stage = DeploymentStage.Completed,
            StartedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class FakeDeploymentService : IDeploymentService
    {
        private readonly DeploymentResult _result;

        public FakeDeploymentService(DeploymentResult result)
        {
            _result = result;
        }

        public int? PortOverride { get; private set; }

        public Task<DeploymentResult> DeployAsync(string appName, string assetUrl, int? portOverride = null, string? operationId = null, CancellationToken cancellationToken = default)
        {
            PortOverride = portOverride;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeExposureService : IExposureService
    {
        private readonly bool _throwOnExpose;

        public FakeExposureService(bool throwOnExpose = false)
        {
            _throwOnExpose = throwOnExpose;
        }

        public int Calls { get; private set; }
        public string Hostname { get; private set; } = string.Empty;

        public Task ExposeAsync(string serviceName, string hostname, string? operationId = null, CancellationToken ct = default)
        {
            Calls++;
            Hostname = hostname;

            if (_throwOnExpose)
            {
                throw new InvalidOperationException("Exposure failed.");
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(string serviceName, string newHostname, string? operationId = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UnexposeAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeDomainNameBuilder : IDomainNameBuilder
    {
        private readonly string _hostname;

        public FakeDomainNameBuilder(string hostname)
        {
            _hostname = hostname;
        }

        public string Build(string label, string domainSuffix) => _hostname;

        public string SanitizeLabel(string value) => value;
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
