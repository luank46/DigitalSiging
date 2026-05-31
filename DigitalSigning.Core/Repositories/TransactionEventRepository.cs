using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MongoDB;
using MongoDB.Driver;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Repository for transaction event logs.
    /// </summary>
    public class TransactionEventRepository : MongoRepository<TransactionEvent>, ITransactionEventRepository
    {
        private const string CollectionName = "transactionEvents";

        public TransactionEventRepository(MongoDbContext context) : base(context, CollectionName)
        {
            // Create a TTL index on CreatedAt for automatic cleanup after 30 days
            var indexKeys = Builders<TransactionEvent>.IndexKeys.Ascending(e => e.CreatedAt);
            var options = new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(30) };
            _collection.Indexes.CreateOne(new CreateIndexModel<TransactionEvent>(indexKeys, options));
        }

        public async Task<IReadOnlyCollection<TransactionEvent>> GetByMaGiaoDichAsync(string maGiaoDich)
        {
            var filter = Builders<TransactionEvent>.Filter.Eq(e => e.MaGiaoDich, maGiaoDich);
            var sort = Builders<TransactionEvent>.Sort.Ascending(e => e.CreatedAt);
            return await _collection.Find(filter).Sort(sort).ToListAsync();
        }

        public override async Task InsertAsync(TransactionEvent ev)
        {
            await base.InsertAsync(ev);
        }
    }
}