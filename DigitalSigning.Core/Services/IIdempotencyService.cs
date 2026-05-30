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
    }
}