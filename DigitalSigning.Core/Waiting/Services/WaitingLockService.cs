using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Waiting.Config;
using DigitalSigning.Core.Waiting.Interfaces;
using DigitalSigning.Core.Waiting.Models;
using StackExchange.Redis;

namespace DigitalSigning.Core.Waiting.Services
{
    /// <summary>
    /// Implementation of <see cref="IWaitingLockService"/> using Redis SETNX for distributed locking.
    /// Each lock key is scoped to a single transactionId with a configurable TTL.
    /// </summary>
    public class WaitingLockService : IWaitingLockService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly WaitingOptions _options;

        public WaitingLockService(IConnectionMultiplexer redis, WaitingOptions options)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IAsyncDisposable?> AcquireLockAsync(string transactionId, TimeSpan ttl, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            var key = $"{_options.LockKeyPrefix}{transactionId}";
            var token = Guid.NewGuid().ToString("N");

            // SETNX with expiry — returns true if the key was set, false if it already exists.
            var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists);
            if (!acquired)
                return null;

            return new RedisLockHandle(_redis, key, token);
        }

        public async Task<bool> IsLockedAsync(string transactionId, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            var key = $"{_options.LockKeyPrefix}{transactionId}";
            return await db.KeyExistsAsync(key);
        }

        /// <summary>
        /// Disposable handle that releases the Redis lock by deleting the key.
        /// Only deletes if the token matches (prevents releasing another worker's lock).
        /// </summary>
        private sealed class RedisLockHandle : IAsyncDisposable
        {
            private readonly IConnectionMultiplexer _redis;
            private readonly string _key;
            private readonly string _token;

            public RedisLockHandle(IConnectionMultiplexer redis, string key, string token)
            {
                _redis = redis;
                _key = key;
                _token = token;
            }

            public async ValueTask DisposeAsync()
            {
                var db = _redis.GetDatabase();
                // Use a Lua script to atomically check token and delete.
                var script = @"
                    if redis.call('GET', KEYS[1]) == ARGV[1] then
                        return redis.call('DEL', KEYS[1])
                    else
                        return 0
                    end";
                await db.ScriptEvaluateAsync(script, new RedisKey[] { _key }, new RedisValue[] { _token });
            }
        }
    }
}
