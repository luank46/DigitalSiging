using System;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MongoDB;
using MongoDB.Driver;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Repository for IdempotencyKey documents.
    /// </summary>
    public class IdempotencyKeyRepository : MongoRepository<IdempotencyKey>, IIdempotencyKeyRepository
    {
        private const string CollectionName = "idempotencyKeys";

        public IdempotencyKeyRepository(MongoDbContext context) : base(context, CollectionName)
        {
            // Ensure a unique index on { TenantId, Key }
            var indexKeys = Builders<IdempotencyKey>.IndexKeys.Ascending(k => k.TenantId).Ascending(k => k.Key);
            var options = new CreateIndexOptions { Unique = true, ExpireAfter = TimeSpan.FromHours(24) };
            _collection.Indexes.CreateOne(new CreateIndexModel<IdempotencyKey>(indexKeys, options));
        }

        public async Task<bool> ExistsAsync(string tenantId, string key)
        {
            var filter = Builders<IdempotencyKey>.Filter.And(
                Builders<IdempotencyKey>.Filter.Eq(k => k.TenantId, tenantId),
                Builders<IdempotencyKey>.Filter.Eq(k => k.Key, key));
            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }

        public async Task<IdempotencyKey?> GetByKeyAsync(string tenantId, string key)
        {
            var filter = Builders<IdempotencyKey>.Filter.And(
                Builders<IdempotencyKey>.Filter.Eq(k => k.TenantId, tenantId),
                Builders<IdempotencyKey>.Filter.Eq(k => k.Key, key));
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public override async Task InsertAsync(IdempotencyKey entity)
        {
            await _collection.InsertOneAsync(entity);
        }
    }

    public interface IIdempotencyKeyRepository : IMongoRepository<IdempotencyKey>
    {
        Task<bool> ExistsAsync(string tenantId, string key);
        Task<IdempotencyKey?> GetByKeyAsync(string tenantId, string key);
    }
}