using ResolveAI.Api.Data;

namespace ResolveAI.Api.Services;

public class SlaNotificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaNotificationBackgroundService> _logger;

    public SlaNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SlaNotificationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCheckAsync(stoppingToken);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var notifications =
                scope.ServiceProvider.GetRequiredService<INotificationService>();
            var audit =
                scope.ServiceProvider.GetRequiredService<IIncidentAuditService>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var timestamp = DateTime.UtcNow;

            await notifications.QueueSlaNotificationsForActiveIncidentsAsync(
                timestamp,
                cancellationToken);

            await audit.RecordSlaEventsForActiveIncidentsAsync(
                timestamp,
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to generate SLA notifications.");
        }
    }
}
