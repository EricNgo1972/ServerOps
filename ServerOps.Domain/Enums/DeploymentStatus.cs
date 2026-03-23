namespace ServerOps.Domain.Enums;

public enum DeploymentStatus
{
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    RolledBack = 5
}
