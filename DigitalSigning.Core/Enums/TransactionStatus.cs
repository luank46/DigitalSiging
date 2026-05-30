using System;

namespace DigitalSigning.Core.Enums
{
    /// <summary>
    /// Trạng thái của giao dịch ký số
    /// </summary>
    public enum TransactionStatus
    {
        /// <summary>
        /// Giao dịch vừa được tạo
        /// </summary>
        Created = 0,

        /// <summary>
        /// Giao dịch đã được đặt vào hàng đợi để xử lý
        /// </summary>
        Queued = 1,

        /// <summary>
        /// Đang chuẩn bị file (tải về, kiểm tra)
        /// </summary>
        PreparingFile = 2,

        /// <summary>
        /// Đang tính hash của file
        /// </summary>
        Hashing = 3,

        /// <summary>
        /// Sẵn sàng để ký (đã có hash, chờ gửi tới CA)
        /// </summary>
        ReadyToSign = 4,

        /// <summary>
        /// Nhà cung cấp CA đang xử lý yêu cầu ký (chờ phản hồi)
        /// </summary>
        ProviderAuthorizing = 5,

        /// <summary>
        /// Đang chờ người dùng xác thực trên ứng dụng di động/web của CA
        /// </summary>
        WaitingUserConfirm = 6,

        /// <summary>
        /// Nhà cung cấp CA đang thực hiện ký
        /// </summary>
        ProviderSigning = 7,

        /// <summary>
        /// Đang nhúng chữ ký vào file (append signature)
        /// </summary>
        AppendingSignature = 8,

        /// <summary>
        /// Đang tải file đã ký lên storage
        /// </summary>
        UploadingSignedFile = 9,

        /// <summary>
        /// Giao dịch đã hoàn thành thành công
        /// </summary>
        Completed = 10,

        /// <summary>
        /// Giao dịch đã thất bại
        /// </summary>
        Failed = 11,

        /// <summary>
        /// Giao dịch đã hết thời gian chờ (timeout)
        /// </summary>
        Timeout = 12,

        /// <summary>
        /// Giao dịch đã bị hủy
        /// </summary>
        Cancelled = 13
    }
}