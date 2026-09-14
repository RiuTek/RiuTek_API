namespace RiuTek.Infrastructure.Services;

public class CartCleanupSettings
{
    public const string SectionName = "CartCleanup";

    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    public int BatchSize { get; set; } = 200;
    public int MaxBatchesPerSweep { get; set; } = 10;
    public const int RetentionDays = 30;

    public void Validate()
    {
        if (IntervalHours < 1 || IntervalHours > 168)
        {
            throw new InvalidOperationException("CartCleanup:IntervalHours must be between 1 and 168 hours.");
        }

        if (BatchSize < 1 || BatchSize > 1000)
        {
            throw new InvalidOperationException("CartCleanup:BatchSize must be between 1 and 1000.");
        }

        if (MaxBatchesPerSweep < 1 || MaxBatchesPerSweep > 100)
        {
            throw new InvalidOperationException("CartCleanup:MaxBatchesPerSweep must be between 1 and 100.");
        }
    }
}
