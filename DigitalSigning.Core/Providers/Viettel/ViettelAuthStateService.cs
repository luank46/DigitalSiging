using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DigitalSigning.Core.Providers.Viettel
{
    /// <summary>
    /// Redis-backed implementation of IViettelAuthStateService.
    /// Stores Viettel OAuth tokens and SAD values per transaction.
    /// </summary>
    public class ViettelAuthStateService : IViettelAuthStateService
    {
        private readonly IDatabase _redis;
        private readonly ILogger<ViettelAuthStateService> _logger;

        // Redis key prefix for Viettel auth state
        private static readonly string KeyPrefix = SignHelper.REDIS_PREFIX_VIETTEL_AUTH;

        // Auth state TTL: 30 minutes (matches Viettel's SAD expiry)
        private static readonly TimeSpan AuthStateTtl = TimeSpan.FromMinutes(30);

        // Lock extension TTL: 2 minutes
        private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(2);

        public ViettelAuthStateService(
            IConnectionMultiplexer redis,
            ILogger<ViettelAuthStateService> logger)
        {
            _redis = redis.GetDatabase();
            _logger = logger;
        }

        public async Task SaveAuthStateAsync(string maGiaoDich, string token, string sad,
            string? serialNumber, CancellationToken ct = default)
        {
            var state = new ViettelAuthState
            {
                Token = token,
                Sad = sad,
                SerialNumber = serialNumber
            };

            var json = JsonSerializer.Serialize(state);
            await _redis.StringSetAsync($"{KeyPrefix}{maGiaoDich}", json, AuthStateTtl);

            _logger.LogDebug("Saved Viettel auth state for {MaGiaoDich}", maGiaoDich);
        }

        public async Task<ViettelAuthState?> GetAuthStateAsync(string maGiaoDich, CancellationToken ct = default)
        {
            var json = await _redis.StringGetAsync($"{KeyPrefix}{maGiaoDich}");
            if (json.IsNullOrEmpty)
            {
                _logger.LogWarning("Viettel auth state not found for {MaGiaoDich}", maGiaoDich);
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ViettelAuthState>(json!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize Viettel auth state for {MaGiaoDich}", maGiaoDich);
                return null;
            }
        }

        public async Task ExtendTransactionLockAsync(string maGiaoDich, CancellationToken ct = default)
        {
            // Extend TTL to keep SAD alive
            await _redis.KeyExpireAsync($"{KeyPrefix}{maGiaoDich}", AuthStateTtl);

            // Store lock extension marker
            var lockKey = $"{SignHelper.REDIS_PREFIX_EXTEND_TX_LOCK}{maGiaoDich}";
            await _redis.StringSetAsync(lockKey, DateTime.UtcNow.ToString("O"), LockTtl);

            _logger.LogTrace("Extended Viettel transaction lock for {MaGiaoDich}", maGiaoDich);
        }

        public async Task ClearAuthStateAsync(string maGiaoDich, CancellationToken ct = default)
        {
            await _redis.KeyDeleteAsync($"{KeyPrefix}{maGiaoDich}");
            await _redis.KeyDeleteAsync($"{SignHelper.REDIS_PREFIX_EXTEND_TX_LOCK}{maGiaoDich}");

            _logger.LogDebug("Cleared Viettel auth state for {MaGiaoDich}", maGiaoDich);
        }
    }
}
