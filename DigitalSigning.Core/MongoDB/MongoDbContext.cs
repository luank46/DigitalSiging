using System;
using MongoDB.Driver;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.MongoDB
{
    /// <summary>
    /// Simple wrapper around MongoClient that provides access to the required collections.
    /// The connection string and database name are supplied via <see cref="MongoDbSettings"/>.
    ///
    /// Tự động tạo indexes khi khởi tạo:
    ///   - transactions: unique index trên maGiaoDich, index trên currentStatus, createdAt
    ///   - transaction_events: index trên maGiaoDich
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

            // Tạo indexes ngay khi khởi tạo
            EnsureIndexes(settings).GetAwaiter().GetResult();
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            return _database.GetCollection<T>(collectionName);
        }

        private async System.Threading.Tasks.Task EnsureIndexes(MongoDbSettings settings)
        {
            try
            {
                // ---- Transactions collection ----
                var txCollection = _database.GetCollection<Transaction>(settings.TransactionCollection);

                // 1. Unique index trên maGiaoDich — chống trùng giao dịch
                var maGiaoDichIndex = new CreateIndexModel<Transaction>(
                    Builders<Transaction>.IndexKeys.Ascending(t => t.MaGiaoDich),
                    new CreateIndexOptions { Unique = true, Name = "idx_maGiaoDich" });
                await txCollection.Indexes.CreateOneAsync(maGiaoDichIndex);

                // 2. Index trên currentStatus — filter transactions theo trạng thái
                var statusIndex = new CreateIndexModel<Transaction>(
                    Builders<Transaction>.IndexKeys.Ascending(t => t.CurrentStatus),
                    new CreateIndexOptions { Name = "idx_currentStatus" });
                await txCollection.Indexes.CreateOneAsync(statusIndex);

                // 3. Index trên createdAt — sort/query theo thời gian
                var createdAtIndex = new CreateIndexModel<Transaction>(
                    Builders<Transaction>.IndexKeys.Descending(t => t.CreatedAt),
                    new CreateIndexOptions { Name = "idx_createdAt" });
                await txCollection.Indexes.CreateOneAsync(createdAtIndex);

                // 4. Compound index: currentStatus + createdAt — query phổ biến
                var statusCreatedAtIndex = new CreateIndexModel<Transaction>(
                    Builders<Transaction>.IndexKeys
                        .Ascending(t => t.CurrentStatus)
                        .Descending(t => t.CreatedAt),
                    new CreateIndexOptions { Name = "idx_status_createdAt" });
                await txCollection.Indexes.CreateOneAsync(statusCreatedAtIndex);

                // 5. Index trên maGiaoDich + currentStatus — cho updateStatus guard
                var txGuardIndex = new CreateIndexModel<Transaction>(
                    Builders<Transaction>.IndexKeys
                        .Ascending(t => t.MaGiaoDich)
                        .Ascending(t => t.CurrentStatus),
                    new CreateIndexOptions { Name = "idx_maGiaoDich_currentStatus" });
                await txCollection.Indexes.CreateOneAsync(txGuardIndex);

                // ---- Transaction Events collection ----
                var eventCollection = _database.GetCollection<TransactionEvent>(settings.EventCollection);

                // 6. Index trên maGiaoDich — query events theo transaction
                var eventTxIndex = new CreateIndexModel<TransactionEvent>(
                    Builders<TransactionEvent>.IndexKeys.Ascending(e => e.MaGiaoDich),
                    new CreateIndexOptions { Name = "idx_event_maGiaoDich" });
                await eventCollection.Indexes.CreateOneAsync(eventTxIndex);

                // 7. TTL index trên createdAt — tự động xóa events sau 90 ngày
                var eventTtlIndex = new CreateIndexModel<TransactionEvent>(
                    Builders<TransactionEvent>.IndexKeys.Ascending(e => e.CreatedAt),
                    new CreateIndexOptions
                    {
                        Name = "idx_event_createdAt_ttl",
                        ExpireAfter = TimeSpan.FromDays(90)
                    });
                await eventCollection.Indexes.CreateOneAsync(eventTtlIndex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"MongoDB index creation failed (non-fatal): {ex.Message}");
                // Không crash — indexes có thể đã tồn tại
            }
        }

        public void Dispose()
        {
            // MongoClient does not need explicit disposal, but we provide the method for completeness.
        }
    }
}
