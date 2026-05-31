using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Distributed file-level lock service (Redis-based).
    /// Khóa theo Md5Hash hoặc URL hash của file gốc để đảm bảo chỉ một người ký
    /// trên cùng một file tại một thời điểm.
    ///
    /// Lock key: "file:lock:{md5Hash}"  hoặc  "file:lock-url:{urlHash}"
    /// TTL mặc định: 5 phút (kịch khung cho 1 transaction ký hoàn chỉnh)
    /// </summary>
    public class FileLockService
    {
        private readonly IDatabase _redis;
        private readonly ILogger<FileLockService> _logger;
        private static readonly TimeSpan DefaultLockTtl = TimeSpan.FromMinutes(5);

        public FileLockService(IConnectionMultiplexer redis, ILogger<FileLockService> logger)
        {
            _redis = redis.GetDatabase();
            _logger = logger;
        }

        /// <summary>
        /// Acquire lock bằng MD5 hash của file. Dùng ở worker layer.
        /// </summary>
        public async Task<LockHandle?> AcquireAsync(string md5Hash, string owner,
            TimeSpan? ttl = null, CancellationToken ct = default)
        {
            var lockKey = $"file:lock:{md5Hash}";
            var token = $"{owner}:{Guid.NewGuid():N}";
            var acquired = await _redis.LockTakeAsync(lockKey, token, ttl ?? DefaultLockTtl);

            if (acquired)
            {
                _logger.LogInformation("FileLock ACQUIRED: md5={Md5}, owner={Owner}", md5Hash, owner);
                return new LockHandle(lockKey, token, _redis, _logger);
            }

            _logger.LogWarning("FileLock REJECTED: md5={Md5}, owner={Owner} — another lock holder exists", md5Hash, owner);
            return null;
        }

        /// <summary>
        /// Acquire lock bằng URL hash của file. Dùng ở API layer (khi chưa có MD5).
        /// </summary>
        public async Task<LockHandle?> AcquireByUrlAsync(string fileUrl, string owner,
            TimeSpan? ttl = null, CancellationToken ct = default)
        {
            var urlHash = ComputeUrlHash(fileUrl);
            var lockKey = $"file:lock-url:{urlHash}";
            var token = $"{owner}:{Guid.NewGuid():N}";
            var acquired = await _redis.LockTakeAsync(lockKey, token, ttl ?? DefaultLockTtl);

            if (acquired)
            {
                _logger.LogInformation("FileLock(URL) ACQUIRED: url={Url}, owner={Owner}", fileUrl, owner);
                return new LockHandle(lockKey, token, _redis, _logger);
            }

            _logger.LogWarning("FileLock(URL) REJECTED: url={Url}, owner={Owner} — another lock holder exists", fileUrl, owner);
            return null;
        }

        /// <summary>
        /// Kiểm tra file có đang bị lock không (theo MD5).
        /// </summary>
        public async Task<bool> IsLockedAsync(string md5Hash)
        {
            return await _redis.KeyExistsAsync($"file:lock:{md5Hash}");
        }

        /// <summary>
        /// Force release lock by Md5Hash.
        /// </summary>
        public async Task ReleaseByMd5Async(string md5Hash)
        {
            var lockKey = $"file:lock:{md5Hash}";
            await _redis.KeyDeleteAsync(lockKey);
            _logger.LogInformation("FileLock RELEASED: md5={Md5}", md5Hash);
        }

        /// <summary>
        /// Force release lock by URL.
        /// </summary>
        public async Task ReleaseByUrlAsync(string fileUrl)
        {
            var urlHash = ComputeUrlHash(fileUrl);
            var lockKey = $"file:lock-url:{urlHash}";
            await _redis.KeyDeleteAsync(lockKey);
            _logger.LogInformation("FileLock(URL) RELEASED: url={Url}", fileUrl);
        }

        /// <summary>
        /// Safe release by MD5 — only deletes the lock key if it contains the expected owner.
        /// Uses Lua EVAL for atomic compare-and-delete to prevent releasing another transaction's lock.
        /// </summary>
        public async Task<bool> SafeReleaseAsync(string md5Hash, string expectedOwner)
        {
            var lockKey = $"file:lock:{md5Hash}";
            var script = @"
                local val = redis.call('GET', KEYS[1])
                if val and val:find(ARGV[1]) then
                    redis.call('DEL', KEYS[1])
                    return 1
                end
                return 0
            ";
            try
            {
                var released = (int)await _redis.ScriptEvaluateAsync(script, new RedisKey[] { lockKey }, new RedisValue[] { expectedOwner });
                if (released == 1)
                    _logger.LogInformation("FileLock SAFE RELEASED: md5={Md5}, owner={Owner}", md5Hash, expectedOwner);
                else
                    _logger.LogWarning("FileLock SAFE RELEASE SKIPPED: md5={Md5}, owner={Owner} — owned by another", md5Hash, expectedOwner);
                return released == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileLock SAFE RELEASE error: md5={Md5} (Redis may be unavailable)", md5Hash);
                return false;
            }
        }

        /// <summary>
        /// Public static helper — computes consistent URL hash for lock key.
        /// Dùng bởi cả FileLockService (acquire) và controller (pre-check).
        /// </summary>
        public static string ComputeUrlHashStatic(string url)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexStringLower(bytes)[..32];
        }

        private static string ComputeUrlHash(string url) => ComputeUrlHashStatic(url);
    }

    /// <summary>
    /// Lock handle — tự động release khi dispose.
    /// </summary>
    public class LockHandle : IAsyncDisposable
    {
        private readonly string _key;
        private readonly string _token;
        private readonly IDatabase _redis;
        private readonly ILogger _logger;
        private bool _disposed;

        public LockHandle(string key, string token, IDatabase redis, ILogger logger)
        {
            _key = key;
            _token = token;
            _redis = redis;
            _logger = logger;
        }

        public async Task ReleaseAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                var released = await _redis.LockReleaseAsync(_key, _token);
                if (released)
                    _logger.LogInformation("FileLock RELEASED: key={Key}", _key);
                else
                    _logger.LogWarning("FileLock RELEASE FAILED: key={Key} — token mismatch or expired", _key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileLock RELEASE error: key={Key} (Redis may be unavailable)", _key);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            GC.SuppressFinalize(this);
        }
    }
}
