using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResolveAI.Api.Data;

namespace ResolveAI.Api.Diagnostics;

/// <summary>
/// Verifies database connectivity for readiness probes.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _context;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(
        AppDbContext context,
        ILogger<DatabaseHealthCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection verified.")
                : HealthCheckResult.Unhealthy("Database connection failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database readiness check threw an exception.");
            return HealthCheckResult.Unhealthy("Database readiness check failed.", ex);
        }
    }
}
