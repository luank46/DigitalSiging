using System;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Service that checks and stores an idempotency key.
    /// Returns Duplicate if a key already exists for the tenant, otherwise stores it and returns Added.
    /// </summary>
    public class IdempotencyService : IIdempotencyService
    {
        private readonly IIdempotencyKeyRepository _repo;

        public IdempotencyService(IIdempotencyKeyRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public async Task<IdempotencyResult> CheckAndStoreAsync(string tenantId, string key, string transactionId)
        {
            if (await _repo.ExistsAsync(tenantId, key))
                return IdempotencyResult.Duplicate;

            var record = new IdempotencyKey
            {
                TenantId = tenantId,
                Key = key,
                TransactionId = transactionId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
            await _repo.InsertAsync(record);
            return IdempotencyResult.Added;
        }
    }
}