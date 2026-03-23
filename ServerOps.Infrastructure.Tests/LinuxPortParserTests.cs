using ServerOps.Infrastructure.Networking.Parsing;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class LinuxPortParserTests
{
    [Fact]
    public void ParseLinuxSs_Returns_Listening_Ports()
    {
        const string output = """
State  Recv-Q Send-Q Local Address:Port  Peer Address:PortProcess
LISTEN 0      511    0.0.0.0:80      0.0.0.0:*    users:(("nginx",pid=321,fd=6))
LISTEN 0      128    127.0.0.1:5000  0.0.0.0:*    users:(("phoebus",pid=777,fd=9))
""";

        var ports = LinuxPortParser.ParseLinuxSs(output);

        Assert.Equal(2, ports.Count);
        Assert.Equal(80, ports[0].Port);
        Assert.Equal(321, ports[0].ProcessId);
        Assert.Equal("nginx", ports[0].ProcessName);
        Assert.Equal(5000, ports[1].Port);
        Assert.Equal(777, ports[1].ProcessId);
        Assert.Equal("phoebus", ports[1].ProcessName);
    }

    [Fact]
    public void ParseLinuxSs_Returns_Empty_For_Empty_Output()
    {
        var ports = LinuxPortParser.ParseLinuxSs(string.Empty);

        Assert.Empty(ports);
    }

    [Fact]
    public void ParseLinuxSs_Ignores_Malformed_Output()
    {
        const string output = """
header
invalid line
another bad row
""";

        var ports = LinuxPortParser.ParseLinuxSs(output);

        Assert.Empty(ports);
    }
}
