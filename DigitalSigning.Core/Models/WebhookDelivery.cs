using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Webhook delivery attempt record.
    /// </summary>
    public class WebhookDelivery
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Transaction identifier (MaGiaoDich)
        /// </summary>
        [BsonElement("transactionId")]
        [JsonPropertyName("transactionId")]
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// Target webhook URL
        /// </summary>
        [BsonElement("url")]
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Payload sent (JSON string)
        /// </summary>
        [BsonElement("payload")]
        [JsonPropertyName("payload")]
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// HTTP status code returned by the webhook endpoint
        /// </summary>
        [BsonElement("statusCode")]
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Response body (if any)
        /// </summary>
        [BsonElement("response")]
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp of the attempt
        /// </summary>
        [BsonElement("attemptedAt")]
        [JsonPropertyName("attemptedAt")]
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of retry attempts made for this webhook
        /// </summary>
        [BsonElement("attemptNumber")]
        [JsonPropertyName("attemptNumber")]
        public int AttemptNumber { get; set; }

        /// <summary>
        /// Flag indicating if the delivery succeeded (status code 2xx)
        /// </summary>
        [BsonElement("succeeded")]
        [JsonPropertyName("succeeded")]
        public bool Succeeded { get; set; }
    }
}