using System;
using System.Collections.Concurrent;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Rate limiter scoped per tenant (school / organization).
    /// </summary>
    public sealed class TenantRateLimiter
    {
        private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _buckets = new();
        private readonly int _capacity;
        private readonly double _refillRatePerSec;

        public TenantRateLimiter(int capacity, double refillRatePerSec)
        {
            _capacity        = capacity;
            _refillRatePerSec = refillRatePerSec;
        }

        public bool TryAcquire(string tenantId)
        {
            var bucket = _buckets.GetOrAdd(tenantId, _ => new TokenBucketRateLimiter(_capacity, _refillRatePerSec));
            return bucket.TryAcquire(tenantId);
        }

        public int AvailableTokens(string tenantId)
        {
            if (_buckets.TryGetValue(tenantId, out var bucket))
                return bucket.AvailableTokens(tenantId);
            return _capacity;
        }
    }
}