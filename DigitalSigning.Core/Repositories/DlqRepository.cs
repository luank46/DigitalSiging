using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MongoDB;
using MongoDB.Driver;
using BsonObjectId = MongoDB.Bson.ObjectId;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Repository for Dead Letter Queue (DLQ) messages.
    /// </summary>
    public class DlqRepository : MongoRepository<DlqMessage>, IDlqRepository
    {
        private const string CollectionName = "dlqMessages";

        public DlqRepository(MongoDbContext context) : base(context, CollectionName)
        {
            // Ensure TTL index on CreatedAt for automatic cleanup after 7 days.
            var indexKeys = Builders<DlqMessage>.IndexKeys.Ascending(m => m.CreatedAt);
            var options = new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) };
            _collection.Indexes.CreateOne(new CreateIndexModel<DlqMessage>(indexKeys, options));
        }

        public override async Task<DlqMessage?> GetByIdAsync(string id)
        {
            var filter = Builders<DlqMessage>.Filter.Eq("_id", new BsonObjectId(id));
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public override async Task<IReadOnlyCollection<DlqMessage>> GetAllAsync()
        {
            var filter = Builders<DlqMessage>.Filter.Empty;
            var cursor = await _collection.FindAsync(filter);
            return await cursor.ToListAsync();
        }

        public async Task<DlqMessage> StoreAsync(DlqMessage entity)
        {
            await base.InsertAsync(entity);
            return entity;
        }
    }

    public interface IDlqRepository : IMongoRepository<DlqMessage>
    {
        Task<DlqMessage> StoreAsync(DlqMessage entity);
    }
}