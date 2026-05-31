using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Redis-backed async cache service.
    /// Mapping từ RedisCacheService cũ. Dùng IConnectionMultiplexer shared.
    /// </summary>
    public class RedisCacheService : IAsyncCacheService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly string _instanceName;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public RedisCacheService(
            IConnectionMultiplexer redis,
            ILogger<RedisCacheService> logger,
            string instanceName = "RemoteSigning_")
        {
            _redis = redis;
            _logger = logger;
            _instanceName = instanceName;
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            try
            {
                var db = _redis.GetDatabase();
                var value = await db.StringGetAsync($"{_instanceName}{key}");
                if (value.IsNullOrEmpty)
                    return null;

                return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis GET failed for key {Key}", key);
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            try
            {
                var db = _redis.GetDatabase();
                var json = JsonSerializer.Serialize(value, JsonOptions);
                await db.StringSetAsync($"{_instanceName}{key}", json, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SET failed for key {Key}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync($"{_instanceName}{key}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis DEL failed for key {Key}", key);
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var db = _redis.GetDatabase();
                return await db.KeyExistsAsync($"{_instanceName}{key}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis EXISTS failed for key {Key}", key);
                return false;
            }
        }

        public async Task ClearAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var endPoints = _redis.GetEndPoints();
                foreach (var endPoint in endPoints)
                {
                    var server = _redis.GetServer(endPoint);
                    var keys = server.Keys(database: db.Database, pattern: $"{_instanceName}*").ToArray();
                    if (keys.Length > 0)
                        await db.KeyDeleteAsync(keys);
                }
                _logger.LogInformation("Redis cache cleared (instance: {Instance})", _instanceName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis CLEAR failed");
            }
        }

        public async Task RemoveByPrefixAsync(string pattern)
        {
            try
            {
                var db = _redis.GetDatabase();
                var endPoints = _redis.GetEndPoints();
                foreach (var endPoint in endPoints)
                {
                    var server = _redis.GetServer(endPoint);
                    var keys = server.Keys(database: db.Database, pattern: $"{_instanceName}{pattern}*").ToArray();
                    if (keys.Length > 0)
                        await db.KeyDeleteAsync(keys);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis REMOVE BY PREFIX failed for pattern {Pattern}", pattern);
            }
        }

        public async Task<TItem?> GetStringAsync<TItem>(string key)
        {
            try
            {
                var db = _redis.GetDatabase();
                var result = await db.StringGetAsync($"{_instanceName}{key}");
                if (result.IsNull) return default;
                return JsonSerializer.Deserialize<TItem>(result.ToString(), JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis GETSTRING failed for key {Key}", key);
                return default;
            }
        }

        public async Task SetStringAsync<TItem>(string key, TItem value, int ttlSeconds)
        {
            try
            {
                var db = _redis.GetDatabase();
                var json = JsonSerializer.Serialize(value, JsonOptions);
                await db.StringSetAsync($"{_instanceName}{key}", json, TimeSpan.FromSeconds(ttlSeconds));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SETSTRING failed for key {Key}", key);
            }
        }
    }
}
