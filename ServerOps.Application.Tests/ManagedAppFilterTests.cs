using ServerOps.Application.Models;
using ServerOps.Application.Services;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class ManagedAppFilterTests
{
    private readonly ManagedAppFilter _filter = new();

    [Fact]
    public async Task FilterAsync_Returns_Only_Matching_App_Services()
    {
        var apps = new[]
        {
            new CompanyApp { Id = "phoebus", Name = "phoebus" },
            new CompanyApp { Id = "ocr", Name = "ocr" }
        };

        var services = new[]
        {
            new ServiceInfo { Name = "phoebus-api", Status = ServiceStatus.Running },
            new ServiceInfo { Name = "phoebus-worker", Status = ServiceStatus.Running },
            new ServiceInfo { Name = "nginx", Status = ServiceStatus.Running },
            new ServiceInfo { Name = "ocr", Status = ServiceStatus.Running }
        };

        var matches = await _filter.FilterAsync(services, apps);

        Assert.Equal(new[] { "phoebus-api", "phoebus-worker", "ocr" }, matches.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task FilterAsync_Is_Case_Insensitive_And_Removes_Service_Suffix()
    {
        var apps = new[]
        {
            new CompanyApp { Id = "phoebus", Name = "PHOEBUS" }
        };

        var services = new[]
        {
            new ServiceInfo { Name = "phoebus.service", Status = ServiceStatus.Running }
        };

        var matches = await _filter.FilterAsync(services, apps);

        var match = Assert.Single(matches);
        Assert.Equal("phoebus.service", match.Name);
    }
}
