using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Models;
using ServerOps.Application.Services;
using ServerOps.Domain.Entities;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class ExposureServiceTests
{
    [Fact]
    public async Task ExposeAsync_Service_Exists_Succeeds()
    {
        var topology = new[]
        {
            new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }
        };
        var registry = new FakeEndpointRegistry();
        var dns = new FakeCloudflareDnsService();
        var config = new FakeCloudflaredConfigService();
        var service = new ExposureService(
            new FakeTopologyService(topology),
            registry,
            new FakeCloudflaredService(new TunnelInfo { IsRunning = true, TunnelId = "abc123" }),
            dns,
            config,
            new FakeOperationLogger());

        await service.ExposeAsync("phoebus-api", "phoebus.local");

        Assert.Equal("phoebus.local", dns.Hostname);
        Assert.Equal("abc123.cfargotunnel.com", dns.Target);
        Assert.Equal("phoebus.local", config.Hostname);
        Assert.Equal(5000, config.Port);
        Assert.True(config.ReloadCalled);
        var mapping = Assert.Single(await registry.GetMappingsAsync());
        Assert.Equal("phoebus-api", mapping.ServiceName);
        Assert.Equal("phoebus.local", mapping.Hostname);
    }

    [Fact]
    public async Task ExposeAsync_No_Port_Fails()
    {
        var service = new ExposureService(
            new FakeTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Ports = [] }]),
            new FakeEndpointRegistry(),
            new FakeCloudflaredService(new TunnelInfo { IsRunning = true, TunnelId = "abc123" }),
            new FakeCloudflareDnsService(),
            new FakeCloudflaredConfigService(),
            new FakeOperationLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExposeAsync("phoebus-api", "phoebus.local"));
    }

    [Fact]
    public async Task ExposeAsync_Tunnel_Down_Fails()
    {
        var service = new ExposureService(
            new FakeTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }]),
            new FakeEndpointRegistry(),
            new FakeCloudflaredService(new TunnelInfo { IsRunning = false, TunnelId = "abc123" }),
            new FakeCloudflareDnsService(),
            new FakeCloudflaredConfigService(),
            new FakeOperationLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExposeAsync("phoebus-api", "phoebus.local"));
    }

    [Fact]
    public async Task UnexposeAsync_Removes_Existing_Mapping()
    {
        var registry = new FakeEndpointRegistry(
            new EndpointMapping { ServiceName = "phoebus-api", Hostname = "phoebus.local" });
        var dns = new FakeCloudflareDnsService();
        var config = new FakeCloudflaredConfigService();
        var service = new ExposureService(
            new FakeTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }]),
            registry,
            new FakeCloudflaredService(new TunnelInfo { IsRunning = true, TunnelId = "abc123" }),
            dns,
            config,
            new FakeOperationLogger());

        await service.UnexposeAsync("phoebus-api");

        Assert.Equal("phoebus.local", dns.DeletedHostname);
        Assert.Equal("phoebus.local", config.RemovedHostname);
        Assert.True(config.ReloadCalled);
        Assert.Empty(await registry.GetMappingsAsync());
    }

    [Fact]
    public async Task UnexposeAsync_NonExisting_Mapping_Does_Not_Error()
    {
        var dns = new FakeCloudflareDnsService();
        var config = new FakeCloudflaredConfigService();
        var service = new ExposureService(
            new FakeTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }]),
            new FakeEndpointRegistry(),
            new FakeCloudflaredService(new TunnelInfo { IsRunning = true, TunnelId = "abc123" }),
            dns,
            config,
            new FakeOperationLogger());

        await service.UnexposeAsync("phoebus-api");

        Assert.Equal(string.Empty, dns.DeletedHostname);
        Assert.Equal(string.Empty, config.RemovedHostname);
        Assert.False(config.ReloadCalled);
    }

    [Fact]
    public async Task UpdateAsync_Replaces_Old_Hostname()
    {
        var registry = new FakeEndpointRegistry(
            new EndpointMapping { ServiceName = "phoebus-api", Hostname = "old.phoebus.local" });
        var dns = new FakeCloudflareDnsService();
        var config = new FakeCloudflaredConfigService();
        var service = new ExposureService(
            new FakeTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Ports = [5000] }]),
            registry,
            new FakeCloudflaredService(new TunnelInfo { IsRunning = true, TunnelId = "abc123" }),
            dns,
            config,
            new FakeOperationLogger());

        await service.UpdateAsync("phoebus-api", "new.phoebus.local");

        Assert.Equal("old.phoebus.local", dns.DeletedHostname);
        Assert.Equal("new.phoebus.local", dns.Hostname);
        Assert.Equal("new.phoebus.local", config.Hostname);
        var mapping = Assert.Single(await registry.GetMappingsAsync());
        Assert.Equal("new.phoebus.local", mapping.Hostname);
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
        private readonly Dictionary<string, EndpointMapping> _mappings = new(StringComparer.OrdinalIgnoreCase);

        public FakeEndpointRegistry(params EndpointMapping[] mappings)
        {
            foreach (var mapping in mappings)
            {
                _mappings[mapping.ServiceName] = mapping;
            }
        }

        public Task<IReadOnlyList<EndpointMapping>> GetMappingsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EndpointMapping>>(_mappings.Values.ToList());

        public Task UpsertAsync(string serviceName, string hostname, CancellationToken ct = default)
        {
            _mappings[serviceName] = new EndpointMapping
            {
                ServiceName = serviceName,
                Hostname = hostname
            };

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string serviceName, CancellationToken ct = default)
        {
            _mappings.Remove(serviceName);
            return Task.CompletedTask;
        }
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

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeCloudflareDnsService : ICloudflareDnsService
    {
        public string Hostname { get; private set; } = string.Empty;
        public string Target { get; private set; } = string.Empty;
        public string DeletedHostname { get; private set; } = string.Empty;

        public Task EnsureCNameAsync(string hostname, string target, CancellationToken ct = default)
        {
            Hostname = hostname;
            Target = target;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string hostname, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task DeleteAsync(string hostname, CancellationToken ct = default)
        {
            DeletedHostname = hostname;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCloudflaredConfigService : ICloudflaredConfigService
    {
        public string Hostname { get; private set; } = string.Empty;
        public string RemovedHostname { get; private set; } = string.Empty;
        public int Port { get; private set; }
        public bool ReloadCalled { get; private set; }

        public Task AddIngressAsync(string hostname, int port, CancellationToken ct = default)
        {
            Hostname = hostname;
            Port = port;
            return Task.CompletedTask;
        }

        public Task RemoveIngressAsync(string hostname, CancellationToken ct = default)
        {
            RemovedHostname = hostname;
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            ReloadCalled = true;
            return Task.CompletedTask;
        }
    }
}
