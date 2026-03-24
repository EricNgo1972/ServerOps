namespace ServerOps.Application.DTOs;

public sealed class OperationLogEvent
{
    public string OperationId { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Line { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
}
