using System;
using System.Collections.Generic;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Request model for CA provider signing operations.
    /// Tương thích với SignRequestModel cũ nhưng không chứa file bytes —
    /// file được tham chiếu qua UnsignedFile.FileByteId (GridFS).
    /// </summary>
    public class ProviderSignRequest
    {
        public string MaGiaoDich { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public ProviderType Provider { get; set; }
        public string? MaNhaPhatHanh { get; set; }

        // ── Authentication ──────────────────────────────────────────────
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? SerialNumber { get; set; }
        public string? MaSoGD { get; set; }
        public string? Token { get; set; }

        // ── Signing config ──────────────────────────────────────────────
        public string? FileType { get; set; }      // XML, PDF, HASH
        public string? SignatureAlgorithm { get; set; }
        public byte[]? SignatureImage { get; set; }
        public string? TypeTextAndImage { get; set; }
        public string? Notification { get; set; }
        public string? Directory { get; set; }
        public string? LoaiKy { get; set; }

        // ── Certificate ─────────────────────────────────────────────────
        public byte[]? Certificate { get; set; }
        public string? CredentialId { get; set; }

        // ── Hash / Signature data ───────────────────────────────────────
        public List<string>? DataHashes { get; set; }
        public List<string>? Hash { get; set; }
        public List<string>? Signatures { get; set; }

        // ── Waiting state ───────────────────────────────────────────────
        public bool IsWaiting { get; set; }
        public string? ProviderSessionId { get; set; }
        public string? TransactionId { get; set; }
        public string? Sad { get; set; }

        // ── File references ─────────────────────────────────────────────
        public List<ProviderFileInfo>? Files { get; set; }

        /// <summary>Số lần thử hiện tại</summary>
        public int Attempt { get; set; }
    }

    /// <summary>
    /// Thông tin một file trong request ký.
    /// </summary>
    public class ProviderFileInfo
    {
        public string? FileByteId { get; set; }       // GridFS reference
        public string? FileUrl { get; set; }           // External URL
        public string? FileName { get; set; }
        public string? Md5Hash { get; set; }
        public string? HashData { get; set; }
        public string? XmlSignature { get; set; }
        public string? NodeSignerPath { get; set; }
        public string? NodeSignerText { get; set; }
        public string? NodeSignerId { get; set; }
        public string? NodeDataSignId { get; set; }
        public List<string>? NodeDataSignIds { get; set; }
        public string? FileNameOutput { get; set; }
    }
}
