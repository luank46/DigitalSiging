using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Dead Letter Queue (DLQ) entry for failed processing attempts.
    /// </summary>
    public class DlqMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Original message identifier (could be RabbitMQ message Id or our internal Id)
        /// </summary>
        [BsonElement("originalId")]
        [JsonPropertyName("originalId")]
        public string OriginalId { get; set; } = string.Empty;

        /// <summary>
        /// The step where processing failed
        /// </summary>
        [BsonElement("failedStep")]
        [JsonPropertyName("failedStep")]
        public TransactionStep FailedStep { get; set; }

        /// <summary>
        /// Error code associated with the failure
        /// </summary>
        [BsonElement("errorCode")]
        [JsonPropertyName("errorCode")]
        public ErrorCode ErrorCode { get; set; }

        /// <summary>
        /// Human-readable error message
        /// </summary>
        [BsonElement("errorMessage")]
        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Serialized payload (could be JSON of the original message)
        /// </summary>
        [BsonElement("payload")]
        [JsonPropertyName("payload")]
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// Number of retry attempts before moving to DLQ
        /// </summary>
        [BsonElement("attemptCount")]
        [JsonPropertyName("attemptCount")]
        public int AttemptCount { get; set; }

        /// <summary>
        /// Timestamp when the message was placed in DLQ
        /// </summary>
        [BsonElement("createdAt")]
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Flag indicating if the DLQ entry has been replayed (processed again)
        /// </summary>
        [BsonElement("replayed")]
        [JsonPropertyName("replayed")]
        public bool Replayed { get; set; }
    }
}