using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RiuTek.Application.Common.Interfaces;
using StackExchange.Redis;

namespace RiuTek.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(
        IConnectionMultiplexer multiplexer,
        RedisSettings settings,
        ILogger<RedisCacheService> logger)
    {
        _multiplexer = multiplexer;
        _settings = settings;
        _logger = logger;
    }

    private string GetFullKey(string key) => $"{_settings.InstanceName}{key}";

    private IDatabase? GetDatabase()
    {
        try
        {
            if (!_multiplexer.IsConnected)
            {
                return null;
            }

            return _multiplexer.GetDatabase();
        }
        catch (Exception ex) when (ex is RedisException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Unable to get Redis database connection: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var db = GetDatabase();
            if (db == null)
            {
                return default;
            }

            var fullKey = GetFullKey(key);
            var value = await db.StringGetAsync(fullKey);

            if (value.IsNullOrEmpty)
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogWarning("Failed to deserialize cached value for key '{Key}': {Message}. Removing invalid entry.", key, ex.Message);
                try
                {
                    await db.KeyDeleteAsync(fullKey);
                }
                catch
                {
                    // Best-effort cleanup
                }
                return default;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Redis error while retrieving key '{Key}': {Message}", key, ex.Message);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (value == null)
        {
            return;
        }

        try
        {
            var db = GetDatabase();
            if (db == null)
            {
                return;
            }

            string json;
            try
            {
                json = JsonSerializer.Serialize(value, JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogWarning("Failed to serialize value for key '{Key}': {Message}", key, ex.Message);
                return;
            }

            var fullKey = GetFullKey(key);
            var ttl = expiration ?? TimeSpan.FromMinutes(_settings.DefaultExpirationMinutes);

            await db.StringSetAsync(fullKey, json, ttl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Redis error while caching key '{Key}': {Message}", key, ex.Message);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var db = GetDatabase();
            if (db == null)
            {
                return;
            }

            var fullKey = GetFullKey(key);
            await db.KeyDeleteAsync(fullKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Redis error while removing key '{Key}': {Message}", key, ex.Message);
        }
    }

    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var db = GetDatabase();
            if (db == null)
            {
                return;
            }

            var fullPattern = $"{_settings.InstanceName}{prefixKey}*";
            var endpoints = _multiplexer.GetEndPoints();

            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var server = _multiplexer.GetServer(endpoint);
                    if (!server.IsConnected)
                    {
                        continue;
                    }

                    var keysBatch = new List<RedisKey>(250);

                    await foreach (var key in server.KeysAsync(pattern: fullPattern, pageSize: 250).WithCancellation(cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        keysBatch.Add(key);

                        if (keysBatch.Count >= 250)
                        {
                            await db.KeyDeleteAsync(keysBatch.ToArray());
                            keysBatch.Clear();
                        }
                    }

                    if (keysBatch.Count > 0)
                    {
                        await db.KeyDeleteAsync(keysBatch.ToArray());
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
                {
                    _logger.LogWarning("Redis error scanning endpoint for prefix '{Prefix}': {Message}", prefixKey, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or SocketException or TimeoutException)
        {
            _logger.LogWarning("Redis error while removing keys by prefix '{Prefix}': {Message}", prefixKey, ex.Message);
        }
    }
}
