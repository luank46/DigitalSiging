using System;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.MessageQueue
{
    /// <summary>
    /// Base class for all domain messages that travel through RabbitMQ.
    /// Contains only lightweight metadata and references — no file bytes or large payloads.
    /// Follows the message contract defined in the architecture plan.
    /// </summary>
    public abstract class BaseMessage
    {
        /// <summary>Unique identifier for this message instance (generated on publish).</summary>
        public Guid MessageId { get; set; } = Guid.NewGuid();

        /// <summary>Transaction identifier (MaGiaoDich) that this message belongs to.</summary>
        public string MaGiaoDich { get; set; } = string.Empty;

        /// <summary>Tenant identifier (school/unit) that initiated the transaction.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>CA provider type for routing.</summary>
        public ProviderType Provider { get; set; }

        /// <summary>Current processing step in the pipeline.</summary>
        public TransactionStep Step { get; set; }

        /// <summary>
        /// Reference to a request payload stored externally (e.g. SignRequestRefId in MongoDB).
        /// Avoids embedding large payloads directly in the message.
        /// </summary>
        public string? RequestRefId { get; set; }

        /// <summary>How many times this message has been attempted (0-based, incremented on each retry).</summary>
        public int Attempt { get; set; }

        /// <summary>OpenTelemetry TraceId for end-to-end tracing across services.</summary>
        public string? TraceId { get; set; }

        /// <summary>UTC timestamp when this message was created/published.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Earliest time this message should be processed (used for delayed retry / waiting scenarios).
        /// If null, process immediately.
        /// </summary>
        public DateTime? NextRunAt { get; set; }
    }

    /// <summary>
    /// Published when files need to be downloaded and prepared for hashing.
    /// </summary>
    public class FilePrepareMessage : BaseMessage
    {
        public string[] FileUrls { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Published when a prepared file is ready for hash calculation.
    /// </summary>
    public class HashMessage : BaseMessage
    {
        /// <summary>Reference to the file record stored in MongoDB (transaction_files_unsigned).</summary>
        public string FileId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Published when a hash is ready and the signing request should be sent to the CA provider.
    /// </summary>
    public class ProviderMessage : BaseMessage
    {
        /// <summary>JSON payload that the CA provider expects (hash data, certificate info, etc.).</summary>
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>
    /// Published when the CA provider returns a "waiting for user confirmation" status.
    /// </summary>
    public class WaitingMessage : BaseMessage
    {
        /// <summary>Absolute deadline after which the waiting period times out.</summary>
        public DateTime Expiration { get; set; }

        /// <summary>CA provider session reference for polling status.</summary>
        public string? ProviderSessionId { get; set; }
    }

    /// <summary>
    /// Published when the CA provider has returned a signed file and the signature needs to be appended/embedded.
    /// </summary>
    public class AppendMessage : BaseMessage
    {
        /// <summary>URL or path to the signed file returned by the CA provider.</summary>
        public string SignedFileUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Published when the signed file with appended signature needs to be uploaded to final storage.
    /// </summary>
    public class UploadMessage : BaseMessage
    {
        /// <summary>Local path to the signed file ready for upload.</summary>
        public string SignedFilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Published when the transaction has completed (or failed) and a webhook notification should be sent.
    /// </summary>
    public class WebhookMessage : BaseMessage
    {
        /// <summary>Target webhook URL to POST the result to.</summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>JSON payload to send in the webhook body.</summary>
        public string Payload { get; set; } = string.Empty;
    }
}