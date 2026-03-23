namespace ServerOps.Application.DTOs;

public sealed class CommandRequest
{
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public bool Allowed { get; init; }
}
