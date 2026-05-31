using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Providers.Viettel
{
    /// <summary>
    /// Manages Viettel authentication state (token + SAD) stored in Redis.
    /// Required because Viettel's SAD (Signature Activation Data) needs to be
    /// kept alive and extended during the waiting/polling phase.
    /// </summary>
    public interface IViettelAuthStateService
    {
        /// <summary>Save auth state for a transaction.</summary>
        Task SaveAuthStateAsync(string maGiaoDich, string token, string sad,
            string? serialNumber, CancellationToken ct = default);

        /// <summary>Get saved auth state.</summary>
        Task<ViettelAuthState?> GetAuthStateAsync(string maGiaoDich, CancellationToken ct = default);

        /// <summary>Extend the transaction lock (keep SAD alive).</summary>
        Task ExtendTransactionLockAsync(string maGiaoDich, CancellationToken ct = default);

        /// <summary>Clear auth state when no longer needed.</summary>
        Task ClearAuthStateAsync(string maGiaoDich, CancellationToken ct = default);
    }

    /// <summary>
    /// Viettel authentication state stored per transaction.
    /// </summary>
    public class ViettelAuthState
    {
        public string? Token { get; set; }
        public string? Sad { get; set; }
        public string? SerialNumber { get; set; }
        public string? CredentialId { get; set; }
    }
}
