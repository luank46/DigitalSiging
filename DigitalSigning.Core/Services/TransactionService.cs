using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Helpers;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Implementation of transaction business logic.
    /// </summary>
    public class TransactionService : ITransactionService
    {
        private readonly ITransactionRepository _transactionRepo;
        private readonly ITransactionEventRepository _eventRepo;

        public TransactionService(ITransactionRepository transactionRepo, ITransactionEventRepository eventRepo)
        {
            _transactionRepo = transactionRepo ?? throw new ArgumentNullException(nameof(transactionRepo));
            _eventRepo = eventRepo ?? throw new ArgumentNullException(nameof(eventRepo));
        }

        public async Task<Transaction?> GetByMaGiaoDichAsync(string maGiaoDich)
        {
            var tx = await _transactionRepo.GetByMaGiaoDichAsync(maGiaoDich);
            if (tx != null && !string.IsNullOrEmpty(tx.Password))
            {
                // Auto-decrypt password when reading
                tx.Password = EncryptionHelper.Decrypt(tx.Password);
            }
            return tx;
        }

        public async Task<Transaction> CreateAsync(Transaction transaction, CancellationToken ct = default)
        {
            transaction.CreatedAt = DateTime.UtcNow;
            transaction.CurrentStatus = TransactionStatus.Created;
            transaction.CurrentStep = TransactionStep.None;
            // Auto-encrypt password before saving
            transaction.Password = EncryptionHelper.Encrypt(transaction.Password);
            await _transactionRepo.InsertAsync(transaction);
            return transaction;
        }

        public async Task UpdateStatusAsync(string maGiaoDich, TransactionStatus status, TransactionStep step)
        {
            await _transactionRepo.UpdateStatusAsync(maGiaoDich, status, step);
        }

        public async Task UpdateFinalStatusAsync(string maGiaoDich, string maTrangThaiKy, string? message, string? sad)
        {
            await _transactionRepo.UpdateFinalStatusAsync(maGiaoDich, maTrangThaiKy, message, sad);
        }

        public async Task RecordEventAsync(string maGiaoDich, TransactionEventType eventType, string details)
        {
            var ev = new TransactionEvent
            {
                MaGiaoDich = maGiaoDich,
                EventType = eventType,
                Status = EventStatus.Completed,
                Details = details,
                CreatedAt = DateTime.UtcNow
            };
            await _eventRepo.InsertAsync(ev);
        }

        public async Task AppendUnsignedFileAsync(string maGiaoDich, UnsignedFile file)
        {
            await _transactionRepo.AppendUnsignedFileAsync(maGiaoDich, file);
        }

        public async Task AppendSignedFileAsync(string maGiaoDich, SignedFile file)
        {
            await _transactionRepo.AppendSignedFileAsync(maGiaoDich, file);
        }

        public async Task SetSignedFilesAsync(string maGiaoDich, List<SignedFile> files)
        {
            await _transactionRepo.SetSignedFilesAsync(maGiaoDich, files);
        }

        public async Task UpdateProviderSessionAsync(string maGiaoDich, string sessionId, DateTime expireAt)
        {
            await _transactionRepo.UpdateProviderSessionAsync(maGiaoDich, sessionId, expireAt);
        }

        public async Task UpdateSignatureDataAsync(string maGiaoDich, List<string> signatures, List<string> dataHashes, List<string> hash)
        {
            await _transactionRepo.UpdateSignatureDataAsync(maGiaoDich, signatures, dataHashes, hash);
        }
    }
}
