namespace RiuTek.Infrastructure.Caching;

public class RedisSettings
{
    public const string SectionName = "RedisSettings";

    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "riutek:";
    public int DefaultExpirationMinutes { get; set; } = 10;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int SyncTimeoutMs { get; set; } = 3000;

    public void Validate()
    {
        if (DefaultExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("Redis DefaultExpirationMinutes must be greater than 0.");
        }

        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "Redis is enabled but ConnectionString is missing or empty. Please configure 'RedisSettings:ConnectionString' or the 'RedisSettings__ConnectionString' environment variable.");
        }

        if (ConnectionString.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
            ConnectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            ConnectionString.Contains("YOUR_REDIS", StringComparison.OrdinalIgnoreCase) ||
            ConnectionString.Contains("DO_NOT_COMMIT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Redis is enabled but ConnectionString contains placeholder text. Please provide a valid Redis connection string via environment variables or user secrets.");
        }
    }
}
