namespace ResolveAI.Api.Services;

public interface IExternalNotificationDeliveryProcessor
{
    Task<int> ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default);
}
