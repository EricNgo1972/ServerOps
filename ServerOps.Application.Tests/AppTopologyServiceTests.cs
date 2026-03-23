using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Services;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class AppTopologyServiceTests
{
    [Fact]
    public async Task GetTopologyAsync_Matches_Single_Port_By_ProcessId()
    {
        var service = CreateService(
            new[]
            {
                new ServiceInfo { Name = "phoebus", Status = ServiceStatus.Running, ProcessId = 123 }
            },
            new[]
            {
                new PortInfo { Port = 5000, ProcessId = 123, ProcessName = "phoebus" }
            });

        var topology = await service.GetTopologyAsync();

        var item = Assert.Single(topology);
        Assert.Equal("phoebus", item.ServiceName);
        Assert.Equal(new[] { 5000 }, item.Ports);
    }

    [Fact]
    public async Task GetTopologyAsync_Matches_Multiple_Ports_By_ProcessId()
    {
        var service = CreateService(
            new[]
            {
                new ServiceInfo { Name = "phoebus", Status = ServiceStatus.Running, ProcessId = 123 }
            },
            new[]
            {
                new PortInfo { Port = 5000, ProcessId = 123, ProcessName = "phoebus" },
                new PortInfo { Port = 5001, ProcessId = 123, ProcessName = "phoebus" }
            });

        var topology = await service.GetTopologyAsync();

        var item = Assert.Single(topology);
        Assert.Equal(new[] { 5000, 5001 }, item.Ports);
    }

    [Fact]
    public async Task GetTopologyAsync_Returns_Empty_Ports_When_Service_Has_No_Pid()
    {
        var service = CreateService(
            new[]
            {
                new ServiceInfo { Name = "phoebus", Status = ServiceStatus.Running, ProcessId = null }
            },
            new[]
            {
                new PortInfo { Port = 5000, ProcessId = 123, ProcessName = "phoebus" }
            });

        var topology = await service.GetTopologyAsync();

        var item = Assert.Single(topology);
        Assert.Empty(item.Ports);
    }

    [Fact]
    public async Task GetTopologyAsync_Ignores_Unmatched_Ports()
    {
        var service = CreateService(
            new[]
            {
                new ServiceInfo { Name = "phoebus", Status = ServiceStatus.Running, ProcessId = 123 }
            },
            new[]
            {
                new PortInfo { Port = 6000, ProcessId = 999, ProcessName = "other" }
            });

        var topology = await service.GetTopologyAsync();

        var item = Assert.Single(topology);
        Assert.Empty(item.Ports);
    }

    private static AppTopologyService CreateService(IReadOnlyList<ServiceInfo> services, IReadOnlyList<PortInfo> ports)
    {
        return new AppTopologyService(new FakeHostService(services), new FakePortService(ports));
    }

    private sealed class FakeHostService : IHostService
    {
        private readonly IReadOnlyList<ServiceInfo> _services;

        public FakeHostService(IReadOnlyList<ServiceInfo> services)
        {
            _services = services;
        }

        public OsType GetCurrentOs() => OsType.Linux;

        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default)
            => Task.FromResult(_services);
    }

    private sealed class FakePortService : IPortService
    {
        private readonly IReadOnlyList<PortInfo> _ports;

        public FakePortService(IReadOnlyList<PortInfo> ports)
        {
            _ports = ports;
        }

        public Task<IReadOnlyList<PortInfo>> GetListeningPortsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_ports);
    }
}
