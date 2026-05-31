using System;

namespace DigitalSigning.Core.Enums
{
    /// <summary>
    /// Mã lỗi trong hệ thống ký số
    /// </summary>
    public enum ErrorCode
    {
        /// <summary>
        /// Không xác định
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Gicit issuance timeout
        /// </summary>
        ProviderTimeout = 100,

        /// <summary>
        /// Wrong signature type
        /// </summary>
        InvalidSignatureType = 101,

        /// <summary>
        /// Signature verification failed
        /// </summary>
        SignatureVerificationFailed = 102,

        /// <summary>
        /// Missing signature data
        /// </summary>
        MissingSignatureData = 103,

        /// <summary>
        /// Queue full
        /// </summary>
        QueueFull = 200,

        /// <summary>
        /// RabbitMQ connection failed
        /// </summary>
        RabbitMqConnectionFailed = 201,

        /// <summary>
        /// MongoDB operation failed
        /// </summary>
        MongoOperationFailed = 300,

        /// <summary>
        /// Redis operation failed
        /// </summary>
        RedisOperationFailed = 301,

        /// <summary>
        /// Idempotency key already exists
        /// </summary>
        DuplicateRequest = 400,

        /// <summary>
        /// Validation error
        /// </summary>
        ValidationError = 401,

        /// <summary>
        /// Operation timed out
        /// </summary>
        Timeout = 402,

        /// <summary>
        /// Permission denied
        /// </summary>
        PermissionDenied = 403,

        /// <summary>
        /// Resource not found
        /// </summary>
        NotFound = 404,

        /// <summary>
        /// Unexpected exception
        /// </summary>
        UnexpectedException = 500,
    }
}