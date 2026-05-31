using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace DigitalSigning.Core.WorkerBase
{
    /// <summary>
    /// Redis-backed implementation of <see cref="IMessageDedupService"/>.
    /// Uses SET key value NX EX to atomically claim a dedup slot.
    /// Default TTL is 5 minutes — long enough to cover retry windows.
    /// </summary>
    public sealed class RedisMessageDedupService : IMessageDedupService
    {
        private readonly IDatabase _redis;

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public RedisMessageDedupService(IConnectionMultiplexer redis)
        {
            _redis = (redis ?? throw new ArgumentNullException(nameof(redis))).GetDatabase();
        }

        public async Task<bool> TryAcquireAsync(string dedupKey, TimeSpan? ttl = null)
        {
            return await _redis.StringSetAsync(
                dedupKey,
                "1",
                ttl ?? DefaultTtl,
                When.NotExists);
        }
    }
}
