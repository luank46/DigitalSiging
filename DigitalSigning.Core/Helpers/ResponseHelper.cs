using System;
using System.Collections.Generic;
using System.Linq;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Helpers
{
    /// <summary>
    /// Helper để xây dựng legacy response models.
    /// Giúp backward-compatible API endpoints đồng bộ format response với client cũ.
    /// </summary>
    public static class ResponseHelper
    {
        /// <summary>
        /// Tạo LegacyResponseData từ transaction.
        /// </summary>
        public static LegacyResponseData FromTransaction(Transaction? transaction, string? tranId = null)
        {
            var response = new LegacyResponseData();

            if (transaction == null)
            {
                response.Status = 404;
                response.Message = "Giao dịch không tồn tại";
                response.Data = "KY_KHONG_THANH_CONG";
                return response;
            }

            response.Status = 200;
            response.Data = transaction.MaTrangThaiKy ?? transaction.CurrentStatus.ToString();
            response.Message = transaction.Message ?? "";
            response.MessageDetail = transaction.MessageDetail ?? "";
            response.Total = transaction.SignedFiles?.Count ?? transaction.UnsignedFiles?.Count ?? 0;

            return response;
        }

        /// <summary>
        /// Tạo LegacyKetQuaGiaoDichResponseModel từ transaction.
        /// </summary>
        public static LegacyKetQuaGiaoDichResponseModel ToKetQuaGiaoDich(Transaction transaction)
        {
            var result = new LegacyKetQuaGiaoDichResponseModel
            {
                MaGiaoDich = transaction.MaGiaoDich,
                MaTrangThaiKy = transaction.MaTrangThaiKy ?? transaction.CurrentStatus.ToString(),
                Message = transaction.Message ?? ""
            };

            if (transaction.SignedFiles != null && transaction.SignedFiles.Count > 0)
            {
                result.ChiTietKetQuas = transaction.SignedFiles.Select(f => new ChiTietKetQua
                {
                    Md5Hash = f.Md5Hash,
                    NodeSignerPath = f.NodeSignerPath,
                    Signature = f.Signature,
                    XmlSignature = f.XmlSignature
                }).ToList();
            }

            return result;
        }

        /// <summary>
        /// Tạo LegacyResponseData thành công.
        /// </summary>
        public static LegacyResponseData Success(object? data = null, string? message = null, int total = 1)
        {
            return new LegacyResponseData
            {
                Status = 200,
                Message = message ?? "Thành công",
                Data = data,
                Total = total
            };
        }

        /// <summary>
        /// Tạo LegacyResponseData lỗi.
        /// </summary>
        public static LegacyResponseData Error(string message, int status = 500, object? data = null)
        {
            return new LegacyResponseData
            {
                Status = status,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// Tạo LegacyResponseData validation error.
        /// </summary>
        public static LegacyResponseData ValidationError(string message)
        {
            return new LegacyResponseData
            {
                Status = 402,
                Message = message,
                Data = "KY_KHONG_THANH_CONG"
            };
        }

        // ── Nested response DTOs ──────────────────────────────────────────

        /// <summary>
        /// Legacy ResponeData format (giống hệt contract cũ).
        /// </summary>
        public class LegacyResponseData
        {
            public int Status { get; set; } = 200;
            public string? Message { get; set; }
            public string? MessageDetail { get; set; }
            public List<string> Messages { get; set; } = new();
            public int Total { get; set; }
            public object? Data { get; set; }
        }

        /// <summary>
        /// Legacy KetQuaGiaoDichResponseModel.
        /// </summary>
        public class LegacyKetQuaGiaoDichResponseModel
        {
            public string? MaTrangThaiKy { get; set; }
            public string? MaGiaoDich { get; set; }
            public string? Message { get; set; }
            public List<ChiTietKetQua>? ChiTietKetQuas { get; set; }
        }

        public class ChiTietKetQua
        {
            public string? Md5Hash { get; set; }
            public string? Signature { get; set; }
            public string? XmlSignature { get; set; }
            public string? NodeSignerPath { get; set; }
        }
    }
}
