using System;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;
using MongoDB.Driver;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Service that checks and stores an idempotency key.
    /// Uses MongoDB unique compound index on {TenantId, Key} for atomic
    /// duplicate detection — no TOCTOU race window.
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
            var record = new IdempotencyKey
            {
                TenantId = tenantId,
                Key = key,
                TransactionId = transactionId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            try
            {
                await _repo.InsertAsync(record);
                return IdempotencyResult.Added;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Unique index on {TenantId, Key} guarantees no race condition.
                // Concurrent insert with same key fails atomically.
                return IdempotencyResult.Duplicate;
            }
        }

        public async Task<IdempotencyKey?> GetByIdempotencyKeyAsync(string tenantId, string key)
        {
            return await _repo.GetByKeyAsync(tenantId, key);
        }

        public async Task DeleteKeyAsync(string tenantId, string key)
        {
            var record = await _repo.GetByKeyAsync(tenantId, key);
            if (record != null)
                await _repo.DeleteAsync(record.Id.ToString());
        }
    }
}