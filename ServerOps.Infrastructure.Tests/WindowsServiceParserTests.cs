using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Host.Parsing;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class WindowsServiceParserTests
{
    [Fact]
    public void Parse_Returns_Structured_Services_For_Valid_Output()
    {
        const string output = """
SERVICE_NAME: Spooler
        TYPE               : 110  WIN32_OWN_PROCESS
        STATE              : 4  RUNNING
        PID                : 321

SERVICE_NAME: W32Time
        TYPE               : 20  WIN32_SHARE_PROCESS
        STATE              : 1  STOPPED
        PID                : 0

SERVICE_NAME: BrokenService
        TYPE               : 10  WIN32_OWN_PROCESS
        STATE              : 7  FAILED
        PID                : 777
""";

        var services = WindowsServiceParser.Parse(output);

        Assert.Equal(3, services.Count);
        Assert.Equal("Spooler", services[0].Name);
        Assert.Equal(ServiceStatus.Running, services[0].Status);
        Assert.Equal(321, services[0].ProcessId);
        Assert.Equal("W32Time", services[1].Name);
        Assert.Equal(ServiceStatus.Stopped, services[1].Status);
        Assert.Null(services[1].ProcessId);
        Assert.Equal("BrokenService", services[2].Name);
        Assert.Equal(ServiceStatus.Failed, services[2].Status);
        Assert.Equal(777, services[2].ProcessId);
    }

    [Fact]
    public void Parse_Returns_Empty_For_Empty_Output()
    {
        var services = WindowsServiceParser.Parse(string.Empty);

        Assert.Empty(services);
    }

    [Fact]
    public void Parse_Ignores_Malformed_Output()
    {
        const string output = """
random text
STATE : UNKNOWN
""";

        var services = WindowsServiceParser.Parse(output);

        Assert.Empty(services);
    }

    [Fact]
    public void Parse_Returns_Unknown_For_Unrecognized_State()
    {
        const string output = """
SERVICE_NAME: PendingService
        STATE              : 2  START_PENDING
""";

        var services = WindowsServiceParser.Parse(output);

        var service = Assert.Single(services);
        Assert.Equal(ServiceStatus.Unknown, service.Status);
    }
}
