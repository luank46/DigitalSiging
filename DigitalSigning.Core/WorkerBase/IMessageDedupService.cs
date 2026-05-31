using System;
using System.Threading.Tasks;

namespace DigitalSigning.Core.WorkerBase
{
    /// <summary>
    /// Service for idempotent message processing — ensures each message is processed only once.
    /// Uses Redis SET NX for atomic dedup with automatic TTL expiry.
    /// </summary>
    public interface IMessageDedupService
    {
        /// <summary>
        /// Attempt to acquire a dedup lock for the given key.
        /// Returns true if this is the first time (message should be processed).
        /// Returns false if already processed (duplicate).
        /// </summary>
        Task<bool> TryAcquireAsync(string dedupKey, TimeSpan? ttl = null);
    }
}
