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
    /// Implementation of <see cref="IWaitingScheduler"/> using a Redis Sorted Set.
    /// Items are stored with NextCheckAtUnixSeconds as the score.
    /// </summary>
    public class WaitingSchedulerService : IWaitingScheduler
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly WaitingOptions _options;

        public WaitingSchedulerService(IConnectionMultiplexer redis, WaitingOptions options)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task ScheduleAsync(WaitingItem item, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(item);
            await db.SortedSetAddAsync(_options.SortedSetKey, item.TransactionId, item.NextCheckAtUnixSeconds);

            // Also store the full item as a hash so we can retrieve metadata without deserializing from the set.
            var hashKey = $"{_options.SortedSetKey}:data:{item.TransactionId}";
            await db.HashSetAsync(hashKey, new HashEntry[]
            {
                new("transactionId", item.TransactionId),
                new("tenantId", item.TenantId),
                new("provider", item.Provider),
                new("providerSessionId", item.ProviderSessionId ?? string.Empty),
                new("nextCheckAtUnixSeconds", item.NextCheckAtUnixSeconds),
                new("timeoutAtUnixSeconds", item.TimeoutAtUnixSeconds),
                new("attemptCount", item.AttemptCount),
                new("createdAt", item.CreatedAt.ToString("O")),
            });
            // Set TTL on the hash data to auto-cleanup
            await db.KeyExpireAsync(hashKey, TimeSpan.FromSeconds(_options.WaitingTimeoutSeconds + 300));
        }

        public async Task RemoveAsync(string transactionId, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            await db.SortedSetRemoveAsync(_options.SortedSetKey, transactionId);
            var hashKey = $"{_options.SortedSetKey}:data:{transactionId}";
            await db.KeyDeleteAsync(hashKey);
        }

        public async Task<WaitingItem[]> GetExpiredItemsAsync(int batchSize = 100, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Get items with score <= now
            var entries = await db.SortedSetRangeByScoreWithScoresAsync(
                _options.SortedSetKey,
                start: double.NegativeInfinity,
                stop: now,
                take: batchSize);

            if (entries == null || entries.Length == 0)
                return Array.Empty<WaitingItem>();

            var items = new WaitingItem[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var transactionId = entries[i].Element.ToString();
                var item = await GetItemDataAsync(transactionId);
                if (item != null)
                {
                    items[i] = item;
                }
                else
                {
                    // Data expired or missing — create a minimal item from the set entry.
                    items[i] = new WaitingItem
                    {
                        TransactionId = transactionId,
                        NextCheckAtUnixSeconds = (long)entries[i].Score,
                    };
                }
            }

            return items;
        }

        public async Task RescheduleAsync(string transactionId, long nextCheckAtUnixSeconds, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();

            // Update score in the sorted set
            await db.SortedSetAddAsync(_options.SortedSetKey, transactionId, nextCheckAtUnixSeconds);

            // Update the hash data
            var hashKey = $"{_options.SortedSetKey}:data:{transactionId}";
            await db.HashIncrementAsync(hashKey, "attemptCount", 1);
            await db.HashSetAsync(hashKey, "nextCheckAtUnixSeconds", nextCheckAtUnixSeconds);
        }

        public async Task<long> GetWaitingCountAsync(CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            return await db.SortedSetLengthAsync(_options.SortedSetKey);
        }

        private async Task<WaitingItem?> GetItemDataAsync(string transactionId)
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{_options.SortedSetKey}:data:{transactionId}";
            var entries = await db.HashGetAllAsync(hashKey);

            if (entries == null || entries.Length == 0)
                return null;

            var dict = new System.Collections.Generic.Dictionary<string, string?>();
            foreach (var entry in entries)
            {
                dict[entry.Name.ToString()] = entry.Value.ToString();
            }

            return new WaitingItem
            {
                TransactionId = dict.GetValueOrDefault("transactionId", transactionId) ?? transactionId,
                TenantId = dict.GetValueOrDefault("tenantId", string.Empty) ?? string.Empty,
                Provider = dict.GetValueOrDefault("provider", string.Empty) ?? string.Empty,
                ProviderSessionId = dict.GetValueOrDefault("providerSessionId", null),
                NextCheckAtUnixSeconds = long.TryParse(dict.GetValueOrDefault("nextCheckAtUnixSeconds", "0"), out var n) ? n : 0,
                TimeoutAtUnixSeconds = long.TryParse(dict.GetValueOrDefault("timeoutAtUnixSeconds", "0"), out var t) ? t : 0,
                AttemptCount = int.TryParse(dict.GetValueOrDefault("attemptCount", "0"), out var a) ? a : 0,
                CreatedAt = DateTime.TryParse(dict.GetValueOrDefault("createdAt", ""), out var d) ? d : DateTime.UtcNow,
            };
        }
    }
}
