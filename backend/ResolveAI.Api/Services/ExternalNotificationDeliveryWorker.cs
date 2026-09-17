namespace ResolveAI.Api.Services;

public class ExternalNotificationDeliveryWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExternalNotificationDeliveryWorker> _logger;

    public ExternalNotificationDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExternalNotificationDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExternalNotificationDeliveryWorker started with polling interval {IntervalSeconds}s.", CheckInterval.TotalSeconds);

        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunBatchAsync(stoppingToken);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ExternalNotificationDeliveryWorker stopped.");
    }

    private async Task RunBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IExternalNotificationDeliveryProcessor>();

            await processor.ProcessPendingDeliveriesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ExternalNotificationDeliveryWorker loop.");
        }
    }
}
