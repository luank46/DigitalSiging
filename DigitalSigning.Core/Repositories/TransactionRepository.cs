using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MongoDB;
using DigitalSigning.Core.Providers;
using MongoDB.Driver;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Concrete MongoDB repository for Transaction documents.
    /// </summary>
    public class TransactionRepository : MongoRepository<Transaction>, ITransactionRepository
    {
        private const string CollectionName = "transactions";

        public TransactionRepository(MongoDbContext context) : base(context, CollectionName)
        {
        }

        public async Task<Transaction?> GetByMaGiaoDichAsync(string maGiaoDich)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpdateStatusAsync(string maGiaoDich, TransactionStatus newStatus, TransactionStep newStep)
        {
            // Guard: không ghi đè Completed/Failed by retry hoặc timeout branch
            var filter = Builders<Transaction>.Filter.And(
                Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich),
                Builders<Transaction>.Filter.Nin(t => t.CurrentStatus, new[]
                    { TransactionStatus.Completed, TransactionStatus.Failed }));

            var update = Builders<Transaction>.Update
                .Set(t => t.CurrentStatus, newStatus)
                .Set(t => t.CurrentStep, newStep)
                .CurrentDate(t => t.UpdatedAt);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateFinalStatusAsync(string maGiaoDich, string maTrangThaiKy, string? message, string? sad)
        {
            // Guard: KHÔNG ghi đè nếu đã DA_KY — chỉ update khi chưa có trạng thái thành công
            var filter = Builders<Transaction>.Filter.And(
                Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich),
                Builders<Transaction>.Filter.Or(
                    Builders<Transaction>.Filter.Ne(t => t.MaTrangThaiKy, SignHelper.MA_TRANG_THAI_DA_KY),
                    Builders<Transaction>.Filter.Eq(t => t.MaTrangThaiKy, null)));

            var update = Builders<Transaction>.Update
                .Set(t => t.MaTrangThaiKy, maTrangThaiKy)
                .Set(t => t.Message, message)
                .Set(t => t.Sad, sad)
                .CurrentDate(t => t.UpdatedAt);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task IncrementRetryCountAsync(string maGiaoDich)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update.Inc(t => t.RetryCount, 1);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task AppendUnsignedFileAsync(string maGiaoDich, UnsignedFile file)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update.Push(t => t.UnsignedFiles, file);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task AppendSignedFileAsync(string maGiaoDich, SignedFile file)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update.Push(t => t.SignedFiles, file);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task SetSignedFilesAsync(string maGiaoDich, List<SignedFile> files)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update.Set(t => t.SignedFiles, files);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateProviderSessionAsync(string maGiaoDich, string sessionId, DateTime expireAt)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update
                .Set(t => t.ProviderSessionId, sessionId)
                .Set(t => t.WaitingExpireAt, expireAt)
                .Set(t => t.CurrentStep, TransactionStep.WaitingUserConfirm)
                .CurrentDate(t => t.UpdatedAt);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateSignatureDataAsync(string maGiaoDich, List<string> signatures, List<string> dataHashes, List<string> hash)
        {
            var filter = Builders<Transaction>.Filter.Eq(t => t.MaGiaoDich, maGiaoDich);
            var update = Builders<Transaction>.Update
                .Set(t => t.Signatures, signatures)
                .Set(t => t.DataHashes, dataHashes)
                .Set(t => t.Hash, hash)
                .CurrentDate(t => t.UpdatedAt);
            await _collection.UpdateOneAsync(filter, update);
        }
    }
}
