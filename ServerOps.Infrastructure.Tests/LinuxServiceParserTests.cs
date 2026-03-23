using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Host.Parsing;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class LinuxServiceParserTests
{
    [Fact]
    public void Parse_Returns_Structured_Services_For_Valid_Output()
    {
        const string output = """
cron.service loaded active running Regular background program processing daemon
ssh.service loaded inactive dead OpenBSD Secure Shell server
broken.service loaded failed failed Broken service
""";

        var services = LinuxServiceParser.Parse(output);

        Assert.Equal(3, services.Count);
        Assert.Equal("cron.service", services[0].Name);
        Assert.Equal(ServiceStatus.Running, services[0].Status);
        Assert.Equal("ssh.service", services[1].Name);
        Assert.Equal(ServiceStatus.Stopped, services[1].Status);
        Assert.Equal("broken.service", services[2].Name);
        Assert.Equal(ServiceStatus.Failed, services[2].Status);
    }

    [Fact]
    public void ParseMainPid_Returns_Pid_When_MainPid_Is_Present()
    {
        const string output = "MainPID=4321";

        var pid = LinuxServiceParser.ParseMainPid(output);

        Assert.Equal(4321, pid);
    }

    [Fact]
    public void ParseServicePidMap_Returns_Service_To_Pid_Map()
    {
        const string output = """
Id=cron.service
MainPID=111

Id=ssh.service
MainPID=0

Id=broken.service
MainPID=999
""";

        var pidMap = LinuxServiceParser.ParseServicePidMap(output);

        Assert.Equal(3, pidMap.Count);
        Assert.Equal(111, pidMap["cron.service"]);
        Assert.Null(pidMap["ssh.service"]);
        Assert.Equal(999, pidMap["broken.service"]);
    }

    [Fact]
    public void Parse_Returns_Empty_For_Empty_Output()
    {
        var services = LinuxServiceParser.Parse(string.Empty);

        Assert.Empty(services);
    }

    [Fact]
    public void Parse_Ignores_Malformed_Output()
    {
        const string output = """
garbage
another invalid line
""";

        var services = LinuxServiceParser.Parse(output);

        Assert.Empty(services);
    }
}
