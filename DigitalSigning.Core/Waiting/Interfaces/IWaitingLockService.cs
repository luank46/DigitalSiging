using System;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Waiting.Interfaces
{
    /// <summary>
    /// Distributed lock service used to prevent multiple Waiting workers from
    /// processing the same transaction concurrently.
    /// Uses Redis SETNX with TTL under the hood.
    /// </summary>
    public interface IWaitingLockService
    {
        /// <summary>
        /// Attempts to acquire a distributed lock for the given transaction.
        /// Returns a disposable handle if the lock was acquired; null if another worker holds it.
        /// The lock auto-expires after <paramref name="ttl"/>.
        /// </summary>
        Task<IAsyncDisposable?> AcquireLockAsync(string transactionId, TimeSpan ttl, CancellationToken ct = default);

        /// <summary>
        /// Returns true if the lock for the given transaction is currently held.
        /// </summary>
        Task<bool> IsLockedAsync(string transactionId, CancellationToken ct = default);
    }
}
