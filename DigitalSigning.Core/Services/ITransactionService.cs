using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Interface for transaction business logic
    /// </summary>
    public interface ITransactionService
    {
        Task<Transaction?> GetByMaGiaoDichAsync(string maGiaoDich);
        Task<Transaction> CreateAsync(Transaction transaction);
        Task UpdateStatusAsync(string maGiaoDich, TransactionStatus status, TransactionStep step);
        Task UpdateFinalStatusAsync(string maGiaoDich, string maTrangThaiKy, string? message, string? sad);
        Task RecordEventAsync(string maGiaoDich, TransactionEventType eventType, string details);
        Task AppendUnsignedFileAsync(string maGiaoDich, UnsignedFile file);
        Task AppendSignedFileAsync(string maGiaoDich, SignedFile file);
        Task SetSignedFilesAsync(string maGiaoDich, System.Collections.Generic.List<SignedFile> files);
        Task UpdateProviderSessionAsync(string maGiaoDich, string sessionId, System.DateTime expireAt);
        Task UpdateSignatureDataAsync(string maGiaoDich, System.Collections.Generic.List<string> signatures, System.Collections.Generic.List<string> dataHashes, System.Collections.Generic.List<string> hash);
    }
}
