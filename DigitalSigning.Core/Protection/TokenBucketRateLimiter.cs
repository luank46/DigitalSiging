using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Token‑bucket rate limiter.
    /// Tokens are added at a fixed rate up to a maximum capacity.
    /// Each call to <see cref="TryAcquire"/> consumes one token.
    /// </summary>
    public sealed class TokenBucketRateLimiter
    {
        private readonly int _capacity;            // max burst size
        private readonly double _refillRatePerSec; // tokens per second
        private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

        public TokenBucketRateLimiter(int capacity, double refillRatePerSec)
        {
            _capacity       = capacity;
            _refillRatePerSec = refillRatePerSec;
        }

        public bool TryAcquire(string key)
        {
            var bucket = _buckets.GetOrAdd(key, _ => new Bucket(_capacity, _refillRatePerSec));
            return bucket.TryTake();
        }

        /// <summary>
        /// Returns how many tokens are currently available (useful for metrics).
        /// </summary>
        public int AvailableTokens(string key)
        {
            if (_buckets.TryGetValue(key, out var b))
            {
                b.Refill();
                return (int)b.CurrentTokens;
            }
            return _capacity;
        }

        // ── internal bucket ──────────────────────────────────────────────
        private sealed class Bucket
        {
            private readonly int _capacity;
            private readonly double _refillRatePerSec;
            private double _tokens;
            private DateTime _lastRefill;

            public Bucket(int capacity, double refillRatePerSec)
            {
                _capacity         = capacity;
                _tokens           = capacity;
                _refillRatePerSec = refillRatePerSec;
                _lastRefill       = DateTime.UtcNow;
            }

            /// <summary>
            /// Current number of tokens available (truncated to int for metric purposes).
            /// </summary>
            public double CurrentTokens
            {
                get { lock (this) { return _tokens; } }
            }

            public bool TryTake()
            {
                lock (this)
                {
                    Refill();
                    if (_tokens >= 1.0)
                    {
                        _tokens -= 1.0;
                        return true;
                    }
                    return false;
                }
            }

            internal void Refill()
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastRefill).TotalSeconds;
                if (elapsed <= 0) return;
                var newTokens = elapsed * _refillRatePerSec;
                _tokens = Math.Min(_capacity, _tokens + newTokens);
                _lastRefill = now;
            }
        }
    }
}