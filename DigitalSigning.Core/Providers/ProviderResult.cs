using System;
using System.Collections.Generic;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Kết quả từ một provider service sau khi xử lý ký.
    /// </summary>
    public class ProviderResult
    {
        /// <summary>Giao dịch đang chờ người dùng xác thực trên app?</summary>
        public bool IsWaiting { get; set; }

        /// <summary>Mã giao dịch</summary>
        public string MaGiaoDich { get; set; } = string.Empty;

        /// <summary>Provider session ID (dùng cho polling)</summary>
        public string? ProviderSessionId { get; set; }

        /// <summary>Thời gian hết hạn chờ xác thực (UTC). Null = không chờ.</summary>
        public DateTime? NextCheckAt { get; set; }

        /// <summary>Chữ ký từ provider (base64). Array index tương ứng với mỗi file.</summary>
        public List<string>? Signatures { get; set; }

        /// <summary>Hash data (đã được provider ký).</summary>
        public List<string>? DataHashes { get; set; }

        /// <summary>Hash computed.</summary>
        public List<string>? Hash { get; set; }

        /// <summary>Danh sách file đã ký.</summary>
        public List<SignedFile>? SignedFiles { get; set; }

        /// <summary>Thông báo lỗi nếu có.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Mã lỗi nếu có.</summary>
        public ErrorCode? ErrorCode { get; set; }

        /// <summary>Trạng thái ký (dạng string legacy: "DA_KY", "KY_KHONG_THANH_CONG"...)</summary>
        public string? MaTrangThaiKy { get; set; }

        /// <summary>SAD (Viettel session token) nếu có.</summary>
        public string? Sad { get; set; }

        /// <summary>Certificate bytes (từ provider).</summary>
        public byte[]? Certificate { get; set; }

        /// <summary>Thành công?</summary>
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
    }
}
