namespace ServerOps.Application.DTOs;

public sealed class CommandResult
{
    public string OperationId { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool Succeeded => ExitCode == 0;
}
