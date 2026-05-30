using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Repository for Transaction documents
    /// </summary>
    public interface ITransactionRepository : IMongoRepository<Transaction>
    {
        Task<Transaction?> GetByMaGiaoDichAsync(string maGiaoDich);
        Task UpdateStatusAsync(string maGiaoDich, TransactionStatus newStatus, TransactionStep newStep);
        Task UpdateFinalStatusAsync(string maGiaoDich, string maTrangThaiKy, string? message, string? sad);
        Task IncrementRetryCountAsync(string maGiaoDich);
        Task AppendUnsignedFileAsync(string maGiaoDich, UnsignedFile file);
        Task AppendSignedFileAsync(string maGiaoDich, SignedFile file);
        Task SetSignedFilesAsync(string maGiaoDich, List<SignedFile> files);
        Task UpdateProviderSessionAsync(string maGiaoDich, string sessionId, DateTime expireAt);
        Task UpdateSignatureDataAsync(string maGiaoDich, List<string> signatures, List<string> dataHashes, List<string> hash);
    }
}