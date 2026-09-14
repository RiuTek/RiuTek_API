namespace RiuTek.Infrastructure.Services;

public interface ICartCleanupService
{
    Task<int> CleanInactiveCartsAsync(
        DateTime nowUtc,
        int? batchSize = null,
        int? maxBatches = null,
        CancellationToken cancellationToken = default);
}
