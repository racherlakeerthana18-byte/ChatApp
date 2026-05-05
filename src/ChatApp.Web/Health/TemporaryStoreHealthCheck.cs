using ChatApp.Infrastructure.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChatApp.Web.Health;

public sealed class TemporaryStoreHealthCheck : IHealthCheck
{
    private readonly ITemporaryStoreDiagnostics _diagnostics;

    public TemporaryStoreHealthCheck(ITemporaryStoreDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var (isHealthy, description) = await _diagnostics.CheckHealthAsync(cancellationToken);
            return isHealthy
                ? HealthCheckResult.Healthy(description)
                : HealthCheckResult.Unhealthy(description);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Temporary store is unavailable.", exception);
        }
    }
}
