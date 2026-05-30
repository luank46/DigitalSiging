using System;
using System.Text.Json.Serialization;

namespace DigitalSigning.Core.Waiting.Models
{
    /// <summary>
    /// Represents a transaction waiting for user confirmation from a CA provider.
    /// Stored as a value in Redis Sorted Set with NextCheckAt as the score.
    /// </summary>
    public class WaitingItem
    {
        /// <summary>
        /// Mã giao dịch (transaction identifier).
        /// </summary>
        [JsonPropertyName("transactionId")]
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// CA provider type.
        /// </summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// CA provider session reference (if the provider returns one during waiting state).
        /// </summary>
        [JsonPropertyName("providerSessionId")]
        public string? ProviderSessionId { get; set; }

        /// <summary>
        /// When to check next (Unix timestamp in seconds). Used as the Sorted Set score.
        /// </summary>
        [JsonPropertyName("nextCheckAtUnixSeconds")]
        public long NextCheckAtUnixSeconds { get; set; }

        /// <summary>
        /// Absolute timeout — if the transaction is still waiting after this time, it will be marked as Timeout.
        /// </summary>
        [JsonPropertyName("timeoutAtUnixSeconds")]
        public long TimeoutAtUnixSeconds { get; set; }

        /// <summary>
        /// Number of times this item has been polled.
        /// </summary>
        [JsonPropertyName("attemptCount")]
        public int AttemptCount { get; set; }

        /// <summary>
        /// UTC timestamp when this waiting item was first created.
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Returns true if the absolute timeout has been exceeded.
        /// </summary>
        public bool IsTimedOut()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= TimeoutAtUnixSeconds;
        }
    }
}
