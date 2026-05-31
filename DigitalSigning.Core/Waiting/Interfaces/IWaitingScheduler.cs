using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Waiting.Models;

namespace DigitalSigning.Core.Waiting.Interfaces
{
    /// <summary>
    /// Schedules transactions that are waiting for user confirmation from CA providers.
    /// Uses Redis Sorted Sets to track NextCheckAt timestamps and process due items in batches.
    /// </summary>
    public interface IWaitingScheduler
    {
        /// <summary>
        /// Enqueues a transaction into the waiting set with the specified NextCheckAt timestamp.
        /// The transaction will be picked up by the polling loop when its time arrives.
        /// </summary>
        Task ScheduleAsync(WaitingItem item, CancellationToken ct = default);

        /// <summary>
        /// Removes a transaction from the waiting set (e.g., when it completes or times out externally).
        /// </summary>
        Task RemoveAsync(string transactionId, CancellationToken ct = default);

        /// <summary>
        /// Gets all transactions whose NextCheckAt has expired (score &lt;= now).
        /// </summary>
        Task<WaitingItem[]> GetExpiredItemsAsync(int batchSize = 100, CancellationToken ct = default);

        /// <summary>
        /// Updates the NextCheckAt timestamp for a transaction already in the set.
        /// </summary>
        Task RescheduleAsync(string transactionId, long nextCheckAtUnixSeconds, CancellationToken ct = default);

        /// <summary>
        /// Returns the count of items currently in the waiting set.
        /// </summary>
        Task<long> GetWaitingCountAsync(CancellationToken ct = default);
    }
}
