using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Infrastructure.Caching;
using RiuTek.Infrastructure.Settings;
using StackExchange.Redis;

namespace RiuTek.Infrastructure.Services;

public class RedisAiQuotaService : IAiQuotaService
{
    private const string ReserveQuotaLuaScript = @"
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
local limit = tonumber(ARGV[1])
local window = tonumber(ARGV[2])

if current >= limit then
    local ttl = redis.call('TTL', KEYS[1])
    if ttl <= 0 then
        ttl = window
    end
    return {0, ttl}
end

local nextVal = redis.call('INCR', KEYS[1])
if nextVal == 1 then
    redis.call('EXPIRE', KEYS[1], window)
end
local ttl = redis.call('TTL', KEYS[1])
if ttl <= 0 then
    ttl = window
end
return {1, ttl}
";

    private readonly IConnectionMultiplexer? _multiplexer;
    private readonly RedisSettings _redisSettings;
    private readonly AiSettings _aiSettings;
    private readonly ILogger<RedisAiQuotaService> _logger;

    public RedisAiQuotaService(
        RedisSettings redisSettings,
        AiSettings aiSettings,
        ILogger<RedisAiQuotaService> logger,
        IConnectionMultiplexer? multiplexer = null)
    {
        _redisSettings = redisSettings;
        _aiSettings = aiSettings;
        _logger = logger;
        _multiplexer = multiplexer;
    }

    public Task<QuotaReservationResult> ReserveTurnAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        var sanitizedClientKey = string.IsNullOrWhiteSpace(clientKey) ? "anonymous" : clientKey.Trim();
        var key = $"{_redisSettings.InstanceName}ai:quota:turn:{sanitizedClientKey}";
        var limit = Math.Max(1, _aiSettings.Limits.TurnsPer15Minutes);
        const int windowSeconds = 15 * 60;

        return EvaluateReservationAsync(key, limit, windowSeconds, "UserTurnLimitExceeded", cancellationToken);
    }

    public Task<QuotaReservationResult> ReserveProviderRequestAsync(CancellationToken cancellationToken = default)
    {
        var utcToday = DateTime.UtcNow;
        var dayKey = utcToday.ToString("yyyyMMdd");
        var key = $"{_redisSettings.InstanceName}ai:quota:provider:{dayKey}";
        var limit = Math.Max(1, _aiSettings.Limits.ProviderRequestsPerDay);
        var secondsUntilNextUtcDay = (int)Math.Max(60, (utcToday.Date.AddDays(1) - utcToday).TotalSeconds);

        return EvaluateReservationAsync(key, limit, secondsUntilNextUtcDay, "DailyProviderLimitExceeded", cancellationToken);
    }

    public Task<QuotaReservationResult> ReserveCustomerTurnAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var key = $"{_redisSettings.InstanceName}ai:quota:customer:{utcNow:yyyyMMdd}";
        var limit = Math.Max(1, _aiSettings.Limits.CustomerTurnsPerDay);
        var secondsUntilNextUtcDay = (int)Math.Max(1, Math.Ceiling((utcNow.Date.AddDays(1) - utcNow).TotalSeconds));
        return EvaluateReservationAsync(key, limit, secondsUntilNextUtcDay, "DailyCustomerTurnLimitExceeded", cancellationToken);
    }

    public Task<QuotaReservationResult> ReserveWebRequestAsync(CancellationToken cancellationToken = default)
    {
        var utcToday = DateTime.UtcNow;
        var dayKey = utcToday.ToString("yyyyMMdd");
        var key = $"{_redisSettings.InstanceName}ai:quota:web:{dayKey}";
        var limit = Math.Max(1, _aiSettings.Limits.WebRequestsPerDay);
        var secondsUntilNextUtcDay = (int)Math.Max(60, (utcToday.Date.AddDays(1) - utcToday).TotalSeconds);

        return EvaluateReservationAsync(key, limit, secondsUntilNextUtcDay, "DailyWebLimitExceeded", cancellationToken);
    }

    private async Task<QuotaReservationResult> EvaluateReservationAsync(
        string fullKey,
        int limit,
        int windowSeconds,
        string limitReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_redisSettings.Enabled || _multiplexer == null || !_multiplexer.IsConnected)
        {
            _logger.LogWarning("AI Quota check blocked because Redis is disabled or disconnected.");
            return new QuotaReservationResult(IsAllowed: false, IsRedisUnavailable: true, ErrorReason: "RedisUnavailable");
        }

        try
        {
            var db = _multiplexer.GetDatabase();
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                ReserveQuotaLuaScript,
                [(RedisKey)fullKey],
                [(RedisValue)limit, (RedisValue)windowSeconds]);

            if (result == null || result.Length < 2)
            {
                return new QuotaReservationResult(IsAllowed: false, IsRedisUnavailable: true, ErrorReason: "InvalidRedisResponse");
            }

            var allowed = (int)result[0] == 1;
            var ttl = (int)result[1];

            if (!allowed)
            {
                return new QuotaReservationResult(
                    IsAllowed: false,
                    IsRedisUnavailable: false,
                    ErrorReason: limitReason,
                    RetryAfterSeconds: Math.Max(1, ttl));
            }

            return new QuotaReservationResult(
                IsAllowed: true,
                IsRedisUnavailable: false,
                RetryAfterSeconds: Math.Max(1, ttl));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Redis error during AI quota reservation: {ErrorType}", ex.GetType().Name);
            return new QuotaReservationResult(IsAllowed: false, IsRedisUnavailable: true, ErrorReason: "RedisError");
        }
    }
}
