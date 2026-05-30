using System;
using MongoDB.Driver;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.MongoDB
{
    /// <summary>
    /// Simple wrapper around MongoClient that provides access to the required collections.
    /// The connection string and database name are supplied via <see cref="MongoDbSettings"/>.
    /// </summary>
    public class MongoDbContext : IDisposable
    {
        private readonly MongoClient _client;
        private readonly IMongoDatabase _database;

        /// <summary>
        /// The underlying MongoDB database instance.
        /// </summary>
        public IMongoDatabase Database => _database;

        public MongoDbContext(MongoDbSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _client = new MongoClient(settings.ConnectionString);
            _database = _client.GetDatabase(settings.DatabaseName);
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            return _database.GetCollection<T>(collectionName);
        }

        public void Dispose()
        {
            // MongoClient does not need explicit disposal, but we provide the method for completeness.
        }
    }
}