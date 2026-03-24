using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Models;
using ServerOps.Application.Services;
using ServerOps.Domain.Entities;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class EndpointServiceTests
{
    [Fact]
    public async Task GetEndpointsAsync_Returns_PublicUrl_For_Mapped_Service()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }],
            [new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" }],
            new TunnelInfo { IsRunning = true });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("phoebus.local", endpoint.Hostname);
        Assert.Equal("https://phoebus.local", endpoint.PublicUrl);
        Assert.True(endpoint.IsExposed);
    }

    [Fact]
    public async Task GetEndpointsAsync_Returns_Null_Url_For_Unmapped_Service()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "unmapped", Ports = [5000] }],
            [],
            new TunnelInfo { IsRunning = true });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.Null(endpoint.PublicUrl);
        Assert.False(endpoint.IsExposed);
    }

    [Fact]
    public async Task GetEndpointsAsync_Uses_First_Port_When_Multiple_Ports_Exist()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000, 5001] }],
            [new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" }],
            new TunnelInfo { IsRunning = true });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal(5000, endpoint.Port);
    }

    [Fact]
    public async Task GetEndpointsAsync_Returns_Not_Exposed_When_Tunnel_Is_Down()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }],
            [new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" }],
            new TunnelInfo { IsRunning = false });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.False(endpoint.IsExposed);
    }

    [Fact]
    public async Task GetEndpointsAsync_Matches_Service_Name_Case_Insensitively_With_Service_Suffix()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "phoebus-api.service", Ports = [5000] }],
            [new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" }],
            new TunnelInfo { IsRunning = true });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("https://phoebus.local", endpoint.PublicUrl);
        Assert.True(endpoint.IsExposed);
    }

    [Fact]
    public async Task GetEndpointsAsync_Returns_Null_Port_When_Service_Has_No_Ports()
    {
        var service = CreateService(
            [new ServiceTopology { ServiceName = "phoebus-api", Ports = [] }],
            [new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" }],
            new TunnelInfo { IsRunning = true });

        var endpoints = await service.GetEndpointsAsync();

        var endpoint = Assert.Single(endpoints);
        Assert.Null(endpoint.Port);
    }

    private static EndpointService CreateService(
        IReadOnlyList<ServiceTopology> topology,
        IReadOnlyList<EndpointMapping> mappings,
        TunnelInfo tunnelInfo)
    {
        return new EndpointService(
            new FakeTopologyService(topology),
            new FakeEndpointRegistry(mappings),
            new FakeCloudflaredService(tunnelInfo));
    }

    private sealed class FakeTopologyService : IAppTopologyService
    {
        private readonly IReadOnlyList<ServiceTopology> _topology;

        public FakeTopologyService(IReadOnlyList<ServiceTopology> topology)
        {
            _topology = topology;
        }

        public Task<IReadOnlyList<ServiceTopology>> GetTopologyAsync(CancellationToken ct = default)
            => Task.FromResult(_topology);
    }

    private sealed class FakeEndpointRegistry : IEndpointRegistry
    {
        private readonly IReadOnlyList<EndpointMapping> _mappings;

        public FakeEndpointRegistry(IReadOnlyList<EndpointMapping> mappings)
        {
            _mappings = mappings;
        }

        public Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default)
            => Task.FromResult(_mappings);

        public Task UpsertAsync(string serviceName, string hostname, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string serviceName, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeCloudflaredService : ICloudflaredService
    {
        private readonly TunnelInfo _tunnelInfo;

        public FakeCloudflaredService(TunnelInfo tunnelInfo)
        {
            _tunnelInfo = tunnelInfo;
        }

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsRunningAsync(CancellationToken cancellationToken = default) => Task.FromResult(_tunnelInfo.IsRunning);

        public Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(_tunnelInfo);

        public Task<CommandResult> InstallAsync(string? operationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult());

        public Task<CommandResult> CreateTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult());

        public Task<CommandResult> StartAsync(string? operationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult());

        public Task<CommandResult> RestartAsync(string? operationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult());

        public Task<CommandResult> DeleteTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult());
    }
}
