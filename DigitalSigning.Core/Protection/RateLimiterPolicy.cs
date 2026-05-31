using System;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Protection
{
    /// <summary>
    /// Strategy interface for rate‑limiting decisions.
    /// Implementations evaluate whether an operation may proceed based on
    /// token bucket, fixed‑window, or provider‑specific rules.
    /// </summary>
    public interface IRateLimiterPolicy
    {
        /// <summary>
        /// Determines whether the caller is within its allowed quota.
        /// </summary>
        bool CanProceed(string key);

        /// <summary>
        /// Records a successful operation so the limiter can adjust its state.
        /// </summary>
        void RecordSuccess(string key);
    }
}