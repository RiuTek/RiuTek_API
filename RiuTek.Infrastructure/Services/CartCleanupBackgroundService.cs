using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RiuTek.Infrastructure.Services;

public class CartCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CartCleanupSettings _settings;
    private readonly ILogger<CartCleanupBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public CartCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        CartCleanupSettings settings,
        ILogger<CartCleanupBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("CartCleanupBackgroundService is disabled by configuration.");
            return;
        }

        // Yield immediately so background service execution never delays host/API startup
        await Task.Yield();

        _logger.LogInformation("CartCleanupBackgroundService started. Running initial sweep...");

        try
        {
            await RunSweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during initial cart cleanup sweep.");
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _settings.IntervalHours));
        using var timer = new PeriodicTimer(interval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during scheduled cart cleanup sweep.");
            }
        }

        _logger.LogInformation("CartCleanupBackgroundService is stopping.");
    }

    private async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<ICartCleanupService>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await cleanupService.CleanInactiveCartsAsync(nowUtc, cancellationToken: cancellationToken);
    }
}
