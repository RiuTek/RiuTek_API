using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RiuTek.Infrastructure.Data;

namespace RiuTek.Infrastructure.Services;

public class CartCleanupService : ICartCleanupService
{
    private readonly ApplicationDbContext _context;
    private readonly CartCleanupSettings _settings;
    private readonly ILogger<CartCleanupService> _logger;

    public CartCleanupService(
        ApplicationDbContext context,
        CartCleanupSettings settings,
        ILogger<CartCleanupService> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> CleanInactiveCartsAsync(
        DateTime nowUtc,
        int? batchSize = null,
        int? maxBatches = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveBatchSize = batchSize.GetValueOrDefault(_settings.BatchSize);
        var effectiveMaxBatches = maxBatches.GetValueOrDefault(_settings.MaxBatchesPerSweep);
        var cutoff = nowUtc.AddDays(-CartCleanupSettings.RetentionDays);

        var totalDeleted = 0;
        var batchCount = 0;

        for (var batch = 0; batch < effectiveMaxBatches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await _context.Carts
                .AsNoTracking()
                .Where(c => (c.UpdatedAt ?? c.CreatedAt) < cutoff)
                .OrderBy(c => c.UpdatedAt ?? c.CreatedAt)
                .Select(c => c.Id)
                .Take(effectiveBatchSize)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                break;
            }

            // Re-verify the cutoff boundary during DELETE to guarantee that any cart
            // mutated concurrently between candidate selection and deletion is preserved.
            var deletedInBatch = await _context.Carts
                .Where(c => candidates.Contains(c.Id) && (c.UpdatedAt ?? c.CreatedAt) < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deletedInBatch;
            batchCount++;

            if (candidates.Count < effectiveBatchSize)
            {
                break;
            }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Cleaned {TotalDeleted} inactive carts in {Batches} batch(es) (cutoff: {Cutoff:u}).",
                totalDeleted,
                batchCount,
                cutoff);
        }

        return totalDeleted;
    }
}
