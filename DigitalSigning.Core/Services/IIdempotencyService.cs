using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Services
{
    public interface IIdempotencyService
    {
        /// <summary>
        /// Checks if the idempotency key exists and if not, stores it.
        /// Returns IdempotencyResult.Duplicate if key exists, IdempotencyResult.Added if stored.
        /// </summary>
        Task<IdempotencyResult> CheckAndStoreAsync(string tenantId, string key, string transactionId);

        /// <summary>
        /// Retrieves the idempotency record for a given key, including the associated transaction ID.
        /// Returns null if no record exists.
        /// </summary>
        Task<IdempotencyKey?> GetByIdempotencyKeyAsync(string tenantId, string key);

        /// <summary>
        /// Deletes an idempotency key record (used when the referenced transaction is stale/deleted).
        /// </summary>
        Task DeleteKeyAsync(string tenantId, string key);
    }
}