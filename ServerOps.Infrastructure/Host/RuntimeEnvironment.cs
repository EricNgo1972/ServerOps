using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.Host;

public sealed class RuntimeEnvironment : IRuntimeEnvironment
{
    private readonly IOptions<PathsOptions> _pathsOptions;

    public RuntimeEnvironment(IOptions<PathsOptions> pathsOptions)
    {
        _pathsOptions = pathsOptions;
    }

    public OsType GetCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return OsType.Windows;
        }

        return OsType.Linux;
    }

    public string GetAppsRootPath()
    {
        var options = _pathsOptions.Value;
        return GetCurrentOs() == OsType.Windows ? options.WindowsAppsRoot : options.LinuxAppsRoot;
    }

    public string GetCloudflaredConfigPath()
    {
        var options = _pathsOptions.Value;
        return GetCurrentOs() == OsType.Windows ? options.WindowsCloudflaredConfigPath : "/etc/cloudflared/config.yml";
    }
}
