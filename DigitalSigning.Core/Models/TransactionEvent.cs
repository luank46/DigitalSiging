using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Sự kiện trong quy trình ký số
    /// </summary>
    public class TransactionEvent
    {
        /// <summary>
        /// Mã định danh sự kiện (UUID)
        /// </summary>
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Mã giao dịch liên kết
        /// </summary>
        [BsonElement("maGiaoDich")]
        [JsonPropertyName("maGiaoDich")]
        public string MaGiaoDich { get; set; } = string.Empty;

        /// <summary>
        /// Loại sự kiện
        /// </summary>
        [BsonElement("eventType")]
        [JsonPropertyName("eventType")]
        public TransactionEventType EventType { get; set; }

        /// <summary>
        /// Thời gian tạo sự kiện
        /// </summary>
        [BsonElement("createdAt")]
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Trạng thái xử lý sự kiện
        /// </summary>
        [BsonElement("status")]
        [JsonPropertyName("status")]
        public EventStatus Status { get; set; }

        /// <summary>
        /// Chi tiết xử lý
        /// </summary>
        [BsonElement("details")]
        [JsonPropertyName("details")]
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Dữ liệu hành động thực hiện
        /// </summary>
        [BsonElement("actionData")]
        [JsonPropertyName("actionData")]
        public ActionData? ActionData { get; set; }
    }

    /// <summary>
    /// Loại sự kiện trong quy trình ký
    /// </summary>
    public enum TransactionEventType
    {
        /// <summary>
        /// Tạo mới giao dịch
        /// </summary>
        TransactionCreated = 1,

        /// <summary>
        /// Cập nhật trạng thái
        /// </summary>
        StatusUpdated = 2,

        /// <summary>
        /// Ký xong (hoàn thành)
        /// </summary>
        TransactionCompleted = 3,

        /// <summary>
        /// Gây lỗi
        /// </summary>
        ErrorOccurred = 4
    }

    /// <summary>
    /// Trạng thái xử lý sự kiện
    /// </summary>
    public enum EventStatus
    {
        /// <summary>
        /// Đã tạo mới
        /// </summary>
        Created = 0,

        /// <summary>
        /// Đã xử lý thành công
        /// </summary>
        Completed = 1,

        /// <summary>
        /// Đã xử lý thất bại
        /// </summary>
        Failed = 2
    }

    /// <summary>
    /// Dữ liệu hành động thực hiện sự kiện
    /// </summary>
    public class ActionData
    {
        /// <summary>
        /// Mã thao tác cụ thể
        /// </summary>
        [JsonPropertyName("actionType")]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Thông tin liên quan đến thao tác
        /// </summary>
        [JsonPropertyName("details")]
        public string Details { get; set; } = string.Empty;
    }
}