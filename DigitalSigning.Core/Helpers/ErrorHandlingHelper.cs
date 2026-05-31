using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DigitalSigning.Core.Helpers
{
    /// <summary>
    /// Error code lookup and management helper.
    /// Maps error codes (string or numeric) to human-readable messages.
    /// Optionally loads from an embedded or external JSON file.
    /// </summary>
    public static class ErrorHandlingHelper
    {
        private static readonly Dictionary<string, string> _defaultErrorMessages = new()
        {
            // ── General errors ──
            ["SUCCESS"] = "Thành công",
            ["UNKNOWN"] = "Lỗi không xác định",
            ["TIMEOUT"] = "Quá thời gian xử lý",
            ["CANCELLED"] = "Giao dịch đã bị hủy",

            // ── Input validation ──
            ["INVALID_PARAMETER"] = "Tham số đầu vào không hợp lệ",
            ["MISSING_TRANSACTION_ID"] = "Thiếu mã giao dịch",
            ["MISSING_USERNAME"] = "Thiếu tên đăng nhập",
            ["MISSING_PASSWORD"] = "Thiếu mật khẩu",
            ["MISSING_SERIAL"] = "Thiếu số Seri chứng thư số",
            ["INVALID_FILE_FORMAT"] = "Định dạng file không hỗ trợ",

            // ── Authentication errors ──
            ["AUTH_FAILED"] = "Xác thực thất bại",
            ["TOKEN_EXPIRED"] = "Token đã hết hạn",
            ["INVALID_CREDENTIALS"] = "Sai thông tin đăng nhập",
            ["SESSION_EXPIRED"] = "Phiên làm việc đã hết hạn",

            // ── Provider errors ──
            ["PROVIDER_ERROR"] = "Lỗi từ nhà cung cấp CA",
            ["PROVIDER_TIMEOUT"] = "Nhà cung cấp CA không phản hồi",
            ["PROVIDER_CONNECTION_FAILED"] = "Kết nối tới CA provider thất bại",
            ["SIGNATURE_FAILED"] = "Quá trình ký số thất bại",
            ["CERTIFICATE_NOT_FOUND"] = "Không tìm thấy chứng thư số",
            ["CERTIFICATE_EXPIRED"] = "Chứng thư số đã hết hạn",
            ["SERIAL_NOT_FOUND"] = "Không tìm thấy Serial Number",
            ["CREDENTIAL_NOT_FOUND"] = "Không tìm thấy thông tin chứng thư",
            ["SAD_EXPIRED"] = "SAD (Signature Activation Data) đã hết hạn",
            ["USER_REJECTED"] = "Người dùng từ chối ký",

            // ── File errors ──
            ["FILE_NOT_FOUND"] = "Không tìm thấy file",
            ["FILE_DOWNLOAD_FAILED"] = "Tải file thất bại",
            ["FILE_UPLOAD_FAILED"] = "Upload file thất bại",
            ["FILE_TOO_LARGE"] = "File vượt quá kích thước cho phép",
            ["FILE_HASH_MISMATCH"] = "Hash của file không khớp",

            // ── Infrastructure errors ──
            ["QUEUE_FULL"] = "Hàng đợi đã đầy",
            ["QUEUE_CONNECTION_FAILED"] = "Kết nối RabbitMQ thất bại",
            ["DB_CONNECTION_FAILED"] = "Kết nối database thất bại",
            ["REDIS_CONNECTION_FAILED"] = "Kết nối Redis thất bại",
            ["DB_WRITE_FAILED"] = "Ghi database thất bại",
            ["DB_READ_FAILED"] = "Đọc database thất bại",

            // ── Status codes ──
            ["DA_KY"] = "Đã ký",
            ["TU_CHOI_KY"] = "Từ chối ký",
            ["KY_KHONG_THANH_CONG"] = "Ký không thành công",
            ["HET_HAN"] = "Đã hết hạn",
            ["CHUNG_THU_SO_HET_HAN"] = "Chứng thư số đã hết hạn",
            ["PENDING"] = "Đang chờ xử lý",
            ["WAITING_USER_CONFIRM"] = "Đang chờ người dùng xác thực",
            ["PROCESSING"] = "Đang xử lý",
            ["COMPLETED"] = "Hoàn thành",
        };

        private static Dictionary<string, string>? _customMessages;

        /// <summary>
        /// Load custom error messages from a JSON file.
        /// JSON format: { "ERROR_CODE": "Error message", ... }
        /// </summary>
        public static void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (entries != null)
                {
                    _customMessages = entries;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load error codes from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Load custom error messages from a JSON string.
        /// </summary>
        public static void LoadFromJson(string json)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (entries != null)
                {
                    _customMessages = entries;
                }
            }
            catch { /* ignore invalid JSON */ }
        }

        /// <summary>
        /// Get human-readable error message for an error code.
        /// Searches custom messages first, then falls back to defaults.
        /// </summary>
        public static string GetErrorMessage(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
                return _defaultErrorMessages.GetValueOrDefault("UNKNOWN", "Lỗi không xác định");

            if (_customMessages != null &&
                _customMessages.TryGetValue(errorCode, out var customMsg))
                return customMsg;

            return _defaultErrorMessages.GetValueOrDefault(errorCode.ToUpperInvariant(),
                $"Lỗi không xác định ({errorCode})");
        }

        /// <summary>
        /// Get error message from an enum value (uses .ToString()).
        /// </summary>
        public static string GetErrorMessage(Enum errorCode)
        {
            return GetErrorMessage(errorCode.ToString());
        }

        /// <summary>
        /// Get all default error codes and messages.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetAllDefaultMessages()
        {
            return _defaultErrorMessages;
        }

        /// <summary>
        /// Get all custom-loaded error messages.
        /// </summary>
        public static IReadOnlyDictionary<string, string>? GetCustomMessages()
        {
            return _customMessages;
        }
    }
}
