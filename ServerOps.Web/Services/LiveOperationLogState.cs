namespace ServerOps.Web.Services;

public sealed class LiveOperationLogState
{
    public event Action? Changed;

    public string OperationId { get; private set; } = string.Empty;
    public string Title { get; private set; } = "Live operation log";
    public bool IsOpen { get; private set; }

    public void Open(string operationId, string title)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return;
        }

        OperationId = operationId.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? "Live operation log" : title.Trim();
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen && string.IsNullOrWhiteSpace(OperationId))
        {
            return;
        }

        OperationId = string.Empty;
        Title = "Live operation log";
        IsOpen = false;
        Changed?.Invoke();
    }
}
