using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Idempotency key to prevent duplicate processing of requests
    /// </summary>
    public class IdempotencyKey
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Unique combination of tenant id and client-provided key
        /// </summary>
        [BsonElement("tenantId")]
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;

        [BsonElement("key")]
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// The transaction id (MaGiaoDich) associated with this key
        /// </summary>
        [BsonElement("transactionId")]
        [JsonPropertyName("transactionId")]
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// When this idempotency record was created
        /// </summary>
        [BsonElement("createdAt")]
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expiration time for idempotency record (default 24 hours)
        /// </summary>
        [BsonElement("expiresAt")]
        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    }
}