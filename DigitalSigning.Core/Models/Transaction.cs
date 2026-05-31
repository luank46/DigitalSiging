using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Core.Models
{
    /// <summary>
    /// Dữ liệu giao dịch ký số — mở rộng với tất cả field từ legacy Thong_Tin_Giao_Dich
    /// để đảm bảo tương thích ngược với client cũ.
    /// </summary>
    public class Transaction
    {
        /// <summary>
        /// Mã định danh giao dịch (UUID)
        /// </summary>
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Mã giao dịch định danh với bên ngoài (GUID)
        /// </summary>
        [BsonElement("maGiaoDich")]
        [JsonPropertyName("maGiaoDich")]
        public string MaGiaoDich { get; set; } = string.Empty;

        /// <summary>
        /// Tên tenant (trường học, đơn vị)
        /// </summary>
        [BsonElement("tenentId")]
        [JsonPropertyName("tenentId")]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Loại nhà cung cấp (VNPT, Viettel, BKAV, GCC, MISA)
        /// </summary>
        [BsonElement("providerType")]
        [JsonPropertyName("providerType")]
        public ProviderType ProviderType { get; set; }

        /// <summary>
        /// Mã nhà phát hành (dạng string, tương thích legacy)
        /// </summary>
        [BsonElement("maNhaPhatHanh")]
        [JsonPropertyName("maNhaPhatHanh")]
        public string? MaNhaPhatHanh { get; set; }

        /// <summary>
        /// Mã trạng thái ký (dạng string để tương thích legacy: "DA_KY", "KY_KHONG_THANH_CONG"...)
        /// </summary>
        [BsonElement("maTrangThaiKy")]
        [JsonPropertyName("maTrangThaiKy")]
        public string? MaTrangThaiKy { get; set; }

        /// <summary>
        /// Thời gian tạo giao dịch
        /// </summary>
        [BsonElement("createdAt")]
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Thời gian hoàn thành giao dịch
        /// </summary>
        [BsonElement("completedAt")]
        [JsonPropertyName("completedAt")]
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Thời gian cập nhật gần nhất
        /// </summary>
        [BsonElement("updatedAt")]
        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Trạng thái hiện tại
        /// </summary>
        [BsonElement("currentStatus")]
        [JsonPropertyName("currentStatus")]
        public TransactionStatus CurrentStatus { get; set; }

        /// <summary>
        /// Bước hiện tại trong quy trình
        /// </summary>
        [BsonElement("currentStep")]
        [JsonPropertyName("currentStep")]
        public TransactionStep CurrentStep { get; set; }

        /// <summary>
        /// Mã trạng thái lỗi nếu có
        /// </summary>
        [BsonElement("errorCode")]
        [JsonPropertyName("errorCode")]
        public ErrorCode? ErrorCode { get; set; }

        /// <summary>
        /// Thông báo lỗi nếu có
        /// </summary>
        [BsonElement("errorMessage")]
        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        // ── Legacy fields ───────────────────────────────────────────────

        /// <summary>Mã năm học</summary>
        [BsonElement("maNamHoc")]
        [JsonPropertyName("maNamHoc")]
        public int MaNamHoc { get; set; }

        /// <summary>Số lượng file trong giao dịch</summary>
        [BsonElement("soLuongFile")]
        [JsonPropertyName("soLuongFile")]
        public int SoLuongFile { get; set; }

        /// <summary>Mã phần mềm (vd: "QUAN_LY_SO_DIEM_VA_HOC_BA")</summary>
        [BsonElement("maPhanMem")]
        [JsonPropertyName("maPhanMem")]
        public string? MaPhanMem { get; set; }

        /// <summary>Mã kiểu ký</summary>
        [BsonElement("maKieuKy")]
        [JsonPropertyName("maKieuKy")]
        public string? MaKieuKy { get; set; }

        /// <summary>Tên đăng nhập CA provider</summary>
        [BsonElement("userName")]
        [JsonPropertyName("userName")]
        public string? UserName { get; set; }

        /// <summary>Số serial chứng thư số</summary>
        [BsonElement("seriNumber")]
        [JsonPropertyName("seriNumber")]
        public string? SeriNumber { get; set; }

        /// <summary>Mật khẩu CA provider</summary>
        [BsonElement("password")]
        [JsonPropertyName("password")]
        public string? Password { get; set; }

        /// <summary>Mô tả thông báo</summary>
        [BsonElement("descriptionNotifi")]
        [JsonPropertyName("descriptionNotifi")]
        public string? DescriptionNotifi { get; set; }

        /// <summary>Ảnh chữ ký (bytes)</summary>
        [BsonElement("imageBytes")]
        [JsonPropertyName("imageBytes")]
        public byte[]? ImageBytes { get; set; }

        /// <summary>Mô tả chữ ký</summary>
        [BsonElement("descriptionSign")]
        [JsonPropertyName("descriptionSign")]
        public string? DescriptionSign { get; set; }

        /// <summary>Loại hiển thị chữ và ảnh</summary>
        [BsonElement("typeTextAndImage")]
        [JsonPropertyName("typeTextAndImage")]
        public string? TypeTextAndImage { get; set; }

        /// <summary>Tên nhân sự</summary>
        [BsonElement("tenNhanSu")]
        [JsonPropertyName("tenNhanSu")]
        public string? TenNhanSu { get; set; }

        /// <summary>Mã nhân sự</summary>
        [BsonElement("maNhanSu")]
        [JsonPropertyName("maNhanSu")]
        public string? MaNhanSu { get; set; }

        /// <summary>Tên trường</summary>
        [BsonElement("tenTruong")]
        [JsonPropertyName("tenTruong")]
        public string? TenTruong { get; set; }

        /// <summary>Mã trường</summary>
        [BsonElement("maTruong")]
        [JsonPropertyName("maTruong")]
        public string? MaTruong { get; set; }

        /// <summary>Danh sách ID hash tài liệu ký</summary>
        [BsonElement("dsIdHashTaiLieuKy")]
        [JsonPropertyName("dsIdHashTaiLieuKy")]
        public List<string>? DsIdHashTaiLieuKy { get; set; }

        /// <summary>Loại ký (XML, PDF, HASH)</summary>
        [BsonElement("loaiKy")]
        [JsonPropertyName("loaiKy")]
        public string? LoaiKy { get; set; }

        /// <summary>Thông báo kết quả</summary>
        [BsonElement("message")]
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Chi tiết thông báo</summary>
        [BsonElement("messageDetail")]
        [JsonPropertyName("messageDetail")]
        public string? MessageDetail { get; set; }

        /// <summary>Mã số GD (phòng giáo dục)</summary>
        [BsonElement("maSoGD")]
        [JsonPropertyName("maSoGD")]
        public string? MaSoGD { get; set; }

        /// <summary>Mã phòng GD</summary>
        [BsonElement("maPhongGD")]
        [JsonPropertyName("maPhongGD")]
        public string? MaPhongGD { get; set; }

        /// <summary>SAD (Viettel session token)</summary>
        [BsonElement("sad")]
        [JsonPropertyName("sad")]
        public string? Sad { get; set; }

        /// <summary>Mã lớp</summary>
        [BsonElement("idLop")]
        [JsonPropertyName("idLop")]
        public decimal? IdLop { get; set; }

        /// <summary>Địa chỉ IP</summary>
        [BsonElement("ipAddress")]
        [JsonPropertyName("ipAddress")]
        public string? IpAddress { get; set; }

        /// <summary>URL webhook callback</summary>
        [BsonElement("urlWebHook")]
        [JsonPropertyName("urlWebHook")]
        public string? UrlWebHook { get; set; }

        // ── Thông tin ký ────────────────────────────────────────────────

        /// <summary>
        /// Dữ liệu chữ ký số
        /// </summary>
        [BsonElement("digitalSignature")]
        [JsonPropertyName("digitalSignature")]
        public DigitalSignature? DigitalSignature { get; set; }

        /// <summary>
        /// Dữ liệu định danh người ký (nếu có)
        /// </summary>
        [BsonElement("signerInfo")]
        [JsonPropertyName("signerInfo")]
        public SignerInfo? SignerInfo { get; set; }

        /// <summary>
        /// Các bước xác thực của nhà cung cấp
        /// </summary>
        [BsonElement("providerAuth")]
        [JsonPropertyName("providerAuth")]
        public ProviderAuthentication? ProviderAuthentication { get; set; }

        /// <summary>
        /// Dữ liệu checksum
        /// </summary>
        [BsonElement("checksum")]
        [JsonPropertyName("checksum")]
        public Checksum? Checksum { get; set; }

        /// <summary>
        /// Flag để tránh idempotency khi retry
        /// </summary>
        [BsonElement("isIdempotent")]
        [JsonPropertyName("isIdempotent")]
        public bool IsIdempotent { get; set; }

        /// <summary>
        /// Số lần thử (retry)
        /// </summary>
        [BsonElement("retryCount")]
        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; }

        // ── Danh sách file ─────────────────────────────────────────────

        /// <summary>Danh sách file chưa ký</summary>
        [BsonElement("unsignedFiles")]
        [JsonPropertyName("unsignedFiles")]
        public List<UnsignedFile>? UnsignedFiles { get; set; }

        /// <summary>Danh sách file đã ký</summary>
        [BsonElement("signedFiles")]
        [JsonPropertyName("signedFiles")]
        public List<SignedFile>? SignedFiles { get; set; }

        /// <summary>Chứng chỉ CA (bytes)</summary>
        [BsonElement("certificate")]
        [JsonPropertyName("certificate")]
        public byte[]? Certificate { get; set; }

        /// <summary>Chuỗi hash dữ liệu (cho provider signing)</summary>
        [BsonElement("dataHashes")]
        [JsonPropertyName("dataHashes")]
        public List<string>? DataHashes { get; set; }

        /// <summary>Hash computed (cho provider signing)</summary>
        [BsonElement("hash")]
        [JsonPropertyName("hash")]
        public List<string>? Hash { get; set; }

        /// <summary>Chữ ký từ provider</summary>
        [BsonElement("signatures")]
        [JsonPropertyName("signatures")]
        public List<string>? Signatures { get; set; }

        /// <summary>Thời gian hết hạn chờ xác thực</summary>
        [BsonElement("waitingExpireAt")]
        [JsonPropertyName("waitingExpireAt")]
        public DateTime? WaitingExpireAt { get; set; }

        /// <summary>Provider session ID (cho polling)</summary>
        [BsonElement("providerSessionId")]
        [JsonPropertyName("providerSessionId")]
        public string? ProviderSessionId { get; set; }

        /// <summary>Md5 hash của file gốc — dùng cho file-level lock</summary>
        [BsonElement("lockMd5Hash")]
        [JsonPropertyName("lockMd5Hash")]
        public string? LockMd5Hash { get; set; }
    }

    // ── Nested models ───────────────────────────────────────────────────

    /// <summary>
    /// File chưa ký (mapping Thong_Tin_Giao_Dich_File_Chua_Ky)
    /// </summary>
    public class UnsignedFile
    {
        [BsonElement("md5Hash")]
        [JsonPropertyName("md5Hash")]
        public string Md5Hash { get; set; } = string.Empty;

        [BsonElement("fileName")]
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [BsonElement("fileNameOutput")]
        [JsonPropertyName("fileNameOutput")]
        public string? FileNameOutput { get; set; }

        [BsonElement("fileUrl")]
        [JsonPropertyName("fileUrl")]
        public string? FileUrl { get; set; }

        [BsonElement("fileBase64")]
        [JsonPropertyName("fileBase64")]
        public string? FileBase64 { get; set; }

        [BsonElement("fileByteId")]
        [JsonPropertyName("fileByteId")]
        public string? FileByteId { get; set; }

        [BsonElement("hashData")]
        [JsonPropertyName("hashData")]
        public string? HashData { get; set; }

        [BsonElement("xmlSignature")]
        [JsonPropertyName("xmlSignature")]
        public string? XmlSignature { get; set; }

        [BsonElement("nodeSignerPath")]
        [JsonPropertyName("nodeSignerPath")]
        public string? NodeSignerPath { get; set; }

        [BsonElement("nodeSignerText")]
        [JsonPropertyName("nodeSignerText")]
        public string? NodeSignerText { get; set; }

        [BsonElement("nodeSignerId")]
        [JsonPropertyName("nodeSignerId")]
        public string? NodeSignerId { get; set; }

        [BsonElement("nodeDataSignId")]
        [JsonPropertyName("nodeDataSignId")]
        public string? NodeDataSignId { get; set; }

        [BsonElement("nodeDataSignIds")]
        [JsonPropertyName("nodeDataSignIds")]
        public List<string>? NodeDataSignIds { get; set; }

        [BsonElement("positionSignatures")]
        [JsonPropertyName("positionSignatures")]
        public List<PositionSignature>? PositionSignatures { get; set; }
    }

    /// <summary>
    /// File đã ký (mapping Thong_Tin_Giao_Dich_File_Da_Ky)
    /// </summary>
    public class SignedFile
    {
        [BsonElement("md5Hash")]
        [JsonPropertyName("md5Hash")]
        public string Md5Hash { get; set; } = string.Empty;

        [BsonElement("fileName")]
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [BsonElement("fileUrl")]
        [JsonPropertyName("fileUrl")]
        public string? FileUrl { get; set; }

        [BsonElement("fileByteId")]
        [JsonPropertyName("fileByteId")]
        public string? FileByteId { get; set; }

        [BsonElement("fileUnsignId")]
        [JsonPropertyName("fileUnsignId")]
        public string? FileUnsignId { get; set; }

        [BsonElement("signature")]
        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        [BsonElement("xmlSignature")]
        [JsonPropertyName("xmlSignature")]
        public string? XmlSignature { get; set; }

        [BsonElement("nodeSignerPath")]
        [JsonPropertyName("nodeSignerPath")]
        public string? NodeSignerPath { get; set; }
    }

    /// <summary>
    /// Vị trí hiển thị chữ ký trên PDF
    /// </summary>
    public class PositionSignature
    {
        [BsonElement("page")]
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [BsonElement("rectangle")]
        [JsonPropertyName("rectangle")]
        public string? Rectangle { get; set; }
    }

    /// <summary>
    /// Thông tin chữ ký số
    /// </summary>
    public class DigitalSignature
    {
        [BsonElement("signatureValue")]
        [JsonPropertyName("signatureValue")]
        public string SignatureValue { get; set; } = string.Empty;

        [BsonElement("signatureAlgorithm")]
        [JsonPropertyName("signatureAlgorithm")]
        public string SignatureAlgorithm { get; set; } = string.Empty;

        [BsonElement("signatureTimestamp")]
        [JsonPropertyName("signatureTimestamp")]
        public DateTime SignatureTimestamp { get; set; }
    }

    /// <summary>
    /// Thông tin người ký
    /// </summary>
    public class SignerInfo
    {
        [BsonElement("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("studentCode")]
        [JsonPropertyName("studentCode")]
        public string StudentCode { get; set; } = string.Empty;

        [BsonElement("email")]
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("phone")]
        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;
    }

    /// <summary>
    /// Thông tin xác thực của nhà cung cấp
    /// </summary>
    public class ProviderAuthentication
    {
        [BsonElement("sessionId")]
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [BsonElement("authTimestamp")]
        [JsonPropertyName("authTimestamp")]
        public DateTime AuthTimestamp { get; set; }

        [BsonElement("authResult")]
        [JsonPropertyName("authResult")]
        public string AuthResult { get; set; } = string.Empty;
    }

    /// <summary>
    /// Checksum dữ liệu
    /// </summary>
    public class Checksum
    {
        [BsonElement("fileHash")]
        [JsonPropertyName("fileHash")]
        public string FileHash { get; set; } = string.Empty;

        [BsonElement("fileSize")]
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [BsonElement("fileName")]
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;
    }
}
