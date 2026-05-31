using System;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Lightweight queue message used across workers.
    /// Contains only metadata and references — no file bytes or large payloads.
    /// Matches the message contract defined in the architecture plan.
    /// </summary>
    public class QueueMessage
    {
        /// <summary>Unique message identifier (Guid string).</summary>
        public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Transaction identifier (MaGiaoDich) that this message belongs to.</summary>
        public string MaGiaoDich { get; set; } = string.Empty;

        /// <summary>Tenant identifier (school/unit).</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>CA provider type for routing.</summary>
        public ProviderType Provider { get; set; }

        /// <summary>Current processing step (enum, not string) for type-safe routing.</summary>
        public TransactionStep Step { get; set; }

        /// <summary>Reference to externally stored payload (e.g. SignRequestRefId in MongoDB).</summary>
        public string? RequestRefId { get; set; }

        /// <summary>How many times this message has been attempted (0-based).</summary>
        public int Attempt { get; set; }

        /// <summary>OpenTelemetry TraceId for end-to-end tracing.</summary>
        public string? TraceId { get; set; }

        /// <summary>UTC timestamp when this message was created.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Earliest time this message should be processed. If null, process immediately.</summary>
        public DateTime? NextRunAt { get; set; }

        /// <summary>Optional lightweight payload (JSON string, limited size).</summary>
        public string? Payload { get; set; }
    }
}