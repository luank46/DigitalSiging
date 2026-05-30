using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.MongoDB;
using MongoDB.Driver;
using BsonObjectId = MongoDB.Bson.ObjectId;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// MongoDB implementation of the generic repository.
    /// It is used as a base class for all concrete repositories.
    /// </summary>
    public class MongoRepository<T> : IMongoRepository<T> where T : class
    {
        protected readonly IMongoCollection<T> _collection;

        public MongoRepository(MongoDbContext context, string collectionName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _collection = context.GetCollection<T>(collectionName);
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            var filter = Builders<T>.Filter.Eq("Id", new BsonObjectId(id));
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public virtual async Task<IReadOnlyCollection<T>> GetAllAsync()
        {
            var cursor = await _collection.FindAsync(FilterDefinition<T>.Empty);
            return await cursor.ToListAsync();
        }

        public virtual async Task InsertAsync(T entity)
        {
            await _collection.InsertOneAsync(entity);
        }

        public virtual async Task UpdateAsync(T entity)
        {
            // Assumes the entity contains an "Id" property of type ObjectId named "Id".
            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty == null) throw new InvalidOperationException("Entity does not expose an Id property.");
            var idValue = idProperty.GetValue(entity);
            var filter = Builders<T>.Filter.Eq("Id", idValue);
            await _collection.ReplaceOneAsync(filter, entity);
        }

        public virtual async Task DeleteAsync(string id)
        {
            var filter = Builders<T>.Filter.Eq("Id", new BsonObjectId(id));
            await _collection.DeleteOneAsync(filter);
        }
    }
}