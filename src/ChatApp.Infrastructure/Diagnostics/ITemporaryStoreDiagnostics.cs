namespace ChatApp.Infrastructure.Diagnostics;

public interface ITemporaryStoreDiagnostics
{
    Task<(bool IsHealthy, string Description)> CheckHealthAsync(CancellationToken cancellationToken = default);
}
