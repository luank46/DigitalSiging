using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Service for handling failures and moving processing to a Dead Letter Queue (DLQ).
    /// </summary>
    public interface IFailureService
    {
        /// <summary>
        /// Stores a failed processing attempt in the DLQ collection.
        /// </summary>
        /// <param name="originalId">Identifier of the original message.</param>
        /// <param name="failedStep">Step at which processing failed.</param>
        /// <param name="errorCode">Error code associated with the failure.</param>
        /// <param name="errorMessage">Human readable error message.</param>
        /// <param name="payload">The original payload (as JSON string).</param>
        Task<DlqMessage> StoreFailureAsync(string originalId, TransactionStep failedStep, ErrorCode errorCode, string errorMessage, string payload);
    }
}