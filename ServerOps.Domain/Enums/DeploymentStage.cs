namespace ServerOps.Domain.Enums;

public enum DeploymentStage
{
    Pending = 1,
    Downloading = 2,
    Extracting = 3,
    ValidatingPackage = 4,
    StoppingService = 5,
    BackingUpCurrent = 6,
    ActivatingNewVersion = 7,
    StartingService = 8,
    VerifyingService = 9,
    VerifyingHealth = 10,
    Completed = 11,
    Failed = 12,
    RolledBack = 13
}
