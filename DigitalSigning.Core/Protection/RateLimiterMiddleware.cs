using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Interface for the composite rate‑limiter.
    /// </summary>
    public interface IRateLimiter
    {
        bool TryAcquire(ProviderType provider, string tenantId);
        void RecordSuccess(ProviderType provider, string tenantId);
    }

    /// <summary>
    /// Composite rate‑limiter that enforces per‑provider AND per‑tenant quotas
    /// before an operation is allowed to proceed.
    /// </summary>
    public sealed class RateLimiter : IRateLimiter
    {
        private readonly ProviderRateLimiter _providerLimiter;
        private readonly TenantRateLimiter  _tenantLimiter;

        public RateLimiter(ProviderRateLimiter providerLimiter, TenantRateLimiter tenantLimiter)
        {
            _providerLimiter = providerLimiter ?? throw new ArgumentNullException(nameof(providerLimiter));
            _tenantLimiter   = tenantLimiter   ?? throw new ArgumentNullException(nameof(tenantLimiter));
        }

        /// <summary>
        /// Checks both provider and tenant quotas. Returns true only if BOTH allow the call.
        /// </summary>
        public bool TryAcquire(ProviderType provider, string tenantId)
        {
            return _providerLimiter.TryAcquire(provider) && _tenantLimiter.TryAcquire(tenantId);
        }

        /// <summary>
        /// Records a successful completion (currently a no‑op for token bucket, but
        /// kept for symmetry with a future sliding‑window implementation).
        /// </summary>
        public void RecordSuccess(ProviderType provider, string tenantId) { }
    }
}