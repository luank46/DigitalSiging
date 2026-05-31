using System;

namespace DigitalSigning.Core.Enums
{
    /// <summary>
    /// Kết quả xử lý idempotency
    /// </summary>
    public enum IdempotencyResult
    {
        /// <summary>
        /// Giao dịch đã tồn tại trong cache idempotency
        /// </summary>
        Duplicate = 0,

        /// <summary>
        /// Giao dịch mới, đã ghi vào DB
        /// </summary>
        Added = 1,

        /// <summary>
        /// Giao dịch bị từ chối vì lý do khác
        /// </summary>
        Rejected = 2
    }
}