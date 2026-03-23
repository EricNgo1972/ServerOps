namespace ServerOps.Application.Abstractions;

public interface ICloudflaredConfigService
{
    Task AddIngressAsync(string hostname, int port, CancellationToken ct = default);
    Task RemoveIngressAsync(string hostname, CancellationToken ct = default);
    Task ReloadAsync(CancellationToken ct = default);
}
