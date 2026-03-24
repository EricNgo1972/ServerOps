using ServerOps.Domain.Enums;

namespace ServerOps.Application.Abstractions;

public interface IRuntimeEnvironment
{
    OsType GetCurrentOs();
    string GetAppsRootPath();
    string GetCloudflaredConfigPath();
    string GetSystemdServiceDirectory();
}
