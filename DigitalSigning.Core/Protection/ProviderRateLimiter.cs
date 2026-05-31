using System;
using System.Collections.Concurrent;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Rate limiter scoped per CA provider (VNPT, Viettel, …).
    /// </summary>
    public sealed class ProviderRateLimiter
    {
        private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _buckets = new();
        private readonly int _capacity;
        private readonly double _refillRatePerSec;

        public ProviderRateLimiter(int capacity, double refillRatePerSec)
        {
            _capacity        = capacity;
            _refillRatePerSec = refillRatePerSec;
        }

        public bool TryAcquire(ProviderType provider)
        {
            var key = provider.ToString();
            var bucket = _buckets.GetOrAdd(key, _ => new TokenBucketRateLimiter(_capacity, _refillRatePerSec));
            return bucket.TryAcquire(key);
        }

        public int AvailableTokens(ProviderType provider)
        {
            var key = provider.ToString();
            if (_buckets.TryGetValue(key, out var bucket))
                return bucket.AvailableTokens(key);
            return _capacity;
        }
    }
}