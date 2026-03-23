using ServerOps.Infrastructure.Networking.Parsing;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class WindowsPortParserTests
{
    [Fact]
    public void ParseWindowsNetstat_Returns_Listening_Tcp_Ports()
    {
        const string output = """
  Proto  Local Address          Foreign Address        State           PID
  TCP    0.0.0.0:80             0.0.0.0:0              LISTENING       321
  TCP    127.0.0.1:5000         0.0.0.0:0              LISTENING       777
  TCP    127.0.0.1:5001         0.0.0.0:0              ESTABLISHED     888
""";

        var ports = WindowsPortParser.ParseWindowsNetstat(output);

        Assert.Equal(2, ports.Count);
        Assert.Equal(80, ports[0].Port);
        Assert.Equal(321, ports[0].ProcessId);
        Assert.Equal(5000, ports[1].Port);
        Assert.Equal(777, ports[1].ProcessId);
    }

    [Fact]
    public void ParseWindowsServicePids_Returns_Pid_Mappings()
    {
        const string output = """
SERVICE_NAME: W3SVC
        TYPE               : 20  WIN32_SHARE_PROCESS
        STATE              : 4  RUNNING
        PID                : 321

SERVICE_NAME: PhoebusApi
        TYPE               : 10  WIN32_OWN_PROCESS
        STATE              : 4  RUNNING
        PID                : 777
""";

        var map = WindowsPortParser.ParseWindowsServicePids(output);

        Assert.Equal("W3SVC", map[321]);
        Assert.Equal("PhoebusApi", map[777]);
    }

    [Fact]
    public void JoinWindowsPorts_Joins_Port_List_With_Service_Names()
    {
        const string netstat = """
  Proto  Local Address          Foreign Address        State           PID
  TCP    0.0.0.0:80             0.0.0.0:0              LISTENING       321
  TCP    127.0.0.1:5000         0.0.0.0:0              LISTENING       777
  TCP    127.0.0.1:6000         0.0.0.0:0              LISTENING       999
""";
        const string services = """
SERVICE_NAME: W3SVC
        PID                : 321

SERVICE_NAME: PhoebusApi
        PID                : 777
""";

        var ports = WindowsPortParser.ParseWindowsNetstat(netstat);
        var servicePids = WindowsPortParser.ParseWindowsServicePids(services);
        var joined = WindowsPortParser.JoinWindowsPorts(ports, servicePids);

        Assert.Equal(3, joined.Count);
        Assert.Equal("W3SVC", joined[0].ProcessName);
        Assert.Equal("PhoebusApi", joined[1].ProcessName);
        Assert.Equal(string.Empty, joined[2].ProcessName);
    }

    [Fact]
    public void ParseWindowsParsers_Return_Empty_For_Empty_Output()
    {
        Assert.Empty(WindowsPortParser.ParseWindowsNetstat(string.Empty));
        Assert.Empty(WindowsPortParser.ParseWindowsServicePids(string.Empty));
    }

    [Fact]
    public void ParseWindowsParsers_Ignore_Malformed_Output()
    {
        const string output = """
garbage line
another bad line
""";

        Assert.Empty(WindowsPortParser.ParseWindowsNetstat(output));
        Assert.Empty(WindowsPortParser.ParseWindowsServicePids(output));
    }
}
