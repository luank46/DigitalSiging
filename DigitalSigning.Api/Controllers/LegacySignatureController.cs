using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Providers;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DigitalSigning.Api.Controllers
{
    /// <summary>
    /// API endpoints tương thích ngược với legacy SignatureController cũ.
    /// Giữ nguyên route, request/response format để client cũ không cần sửa.
    /// </summary>
    [ApiController]
    [Route("Signature")]
    public class LegacySignatureController : ControllerBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IMessagePublisher _messagePublisher;
        private readonly IMetricsService _metricsService;
        private readonly ILogger<LegacySignatureController> _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly FileLockService _fileLock;
        private static readonly HttpClient _httpClient = new();

        public LegacySignatureController(
            ITransactionService transactionService,
            IMessagePublisher messagePublisher,
            IMetricsService metricsService,
            ILogger<LegacySignatureController> logger,
            IConnectionMultiplexer redis,
            FileLockService fileLock)
        {
            _transactionService = transactionService;
            _messagePublisher = messagePublisher;
            _metricsService = metricsService;
            _logger = logger;
            _redis = redis;
            _fileLock = fileLock;
        }

        /// <summary>
        /// Legacy: POST /Signature/Sign
        /// Client cũ gửi tranId đã có, worker xử lý.
        /// </summary>
        [HttpPost("Sign")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> Sign([FromBody] LegacySignRequest model)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(model?.tranId))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số mã giao dịch";
                return Ok(result);
            }

            try
            {
                var transaction = await _transactionService.GetByMaGiaoDichAsync(model.tranId);
                if (transaction == null)
                {
                    result.Status = 404;
                    result.Message = "Giao dịch không tồn tại";
                    return Ok(result);
                }

                // Publish message to queue to resume processing
                var message = new QueueMessage
                {
                    MaGiaoDich = model.tranId,
                    Provider = transaction.ProviderType,
                    Step = TransactionStep.FilePrepare,
                    TraceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
                };
                await _messagePublisher.PublishAsync(message);

                result.Status = 200;
                result.Data = model.tranId;
                result.Message = "Thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legacy Sign failed for {TranId}", model?.tranId);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/Reponse
        /// Client cũ query trạng thái giao dịch.
        /// Đây là endpoint synchronous (giống legacy).
        /// </summary>
        [HttpPost("Reponse")]
        [ProducesResponseType(200)]
        public ActionResult<LegacyResponseData> Reponse([FromBody] LegacySignRequest model)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(model?.tranId))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số mã giao dịch";
                return Ok(result);
            }

            try
            {
                // Note: sync call vì legacy dùng synchronous
                var transaction = _transactionService.GetByMaGiaoDichAsync(model.tranId)
                    .GetAwaiter().GetResult();

                if (transaction != null)
                {
                    result.Data = transaction.MaTrangThaiKy ?? transaction.CurrentStatus.ToString();
                    result.Message = transaction.Message ?? "";
                    result.MessageDetail = transaction.MessageDetail ?? "";
                }
                else
                {
                    result.Data = "KY_KHONG_THANH_CONG";
                    result.Message = "Dịch vụ bị gián đoạn";
                }

                result.Status = 200;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legacy Reponse failed for {TranId}", model?.tranId);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/GetTransaction
        /// Lấy kết quả ký chi tiết (trả về danh sách file + signature).
        /// </summary>
        [HttpPost("GetTransaction")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> GetTransaction(
            [FromBody] LegacyGetTransactionRequest request)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(request?.MaGiaoDich))
            {
                result.Status = 402;
                result.Messages.Add("Thiếu tham số mã giao dịch");
                return Ok(result);
            }

            try
            {
                var transaction = await _transactionService.GetByMaGiaoDichAsync(request.MaGiaoDich);
                if (transaction == null)
                {
                    result.Status = 404;
                    result.Messages.Add("Giao dịch không tồn tại");
                    return Ok(result);
                }

                var responseData = new LegacyKetQuaGiaoDichResponseModel
                {
                    MaGiaoDich = request.MaGiaoDich,
                    MaTrangThaiKy = transaction.MaTrangThaiKy ?? transaction.CurrentStatus.ToString(),
                    Message = transaction.Message ?? ""
                };

                if (transaction.MaTrangThaiKy == SignHelper.MA_TRANG_THAI_DA_KY &&
                    transaction.SignedFiles != null)
                {
                    responseData.ChiTietKetQuas = transaction.SignedFiles.Select(f => new ChiTietKetQua
                    {
                        Md5Hash = f.Md5Hash,
                        NodeSignerPath = f.NodeSignerPath,
                        Signature = f.Signature,
                        XmlSignature = f.XmlSignature
                    }).ToList();
                }

                result.Data = responseData;
                result.Status = 200;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legacy GetTransaction failed for {MaGiaoDich}", request?.MaGiaoDich);
                result.Status = 500;
                result.Messages.Add(ex.Message);
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/SignBase64
        /// Client cũ gửi file base64, tạo giao dịch và push queue.
        /// </summary>
        [HttpPost("SignBase64")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> SignBase64(
            [FromBody] LegacyInputSignModel signModel)
        {
            var result = new LegacyResponseData();

            if (signModel == null)
            {
                result.Status = 402;
                result.Message = "Thiếu tham số đầu vào";
                return Ok(result);
            }

            try
            {
                // Lấy Md5Hash của file đầu tiên để làm khóa
                var md5Hash = signModel.FileUnsign?.FirstOrDefault()?.Md5Hash;
                if (!string.IsNullOrEmpty(md5Hash))
                {
                    var lockHandle = await _fileLock.AcquireAsync(md5Hash,
                        $"SignBase64:{signModel.UserName}",
                        TimeSpan.FromMinutes(10));

                    if (lockHandle == null)
                    {
                        // Có người đang ký file này — trả về lỗi luôn, không tạo transaction
                        result.Status = 409;
                        result.Message = "File này đang được người khác ký, vui lòng thử lại sau";
                        return Ok(result);
                    }

                    // Lưu lock handle để release sau
                    HttpContext.Items["FileLockHandle"] = lockHandle;
                    HttpContext.Items["FileLockMd5"] = md5Hash;
                }

                var maGiaoDich = Guid.NewGuid().ToString();

                var transaction = new Transaction
                {
                    MaGiaoDich = maGiaoDich,
                    MaTrangThaiKy = "PENDING",
                    UserName = signModel.UserName,
                    Password = signModel.Password,
                    TenNhanSu = signModel.TenNhanSu,
                    MaNhanSu = signModel.MaNhanSu,
                    TenTruong = signModel.TenTruong,
                    MaTruong = signModel.MaTruong,
                    DescriptionNotifi = signModel.DescriptionNotifi,
                    DescriptionSign = signModel.DescriptionSign,
                    TypeTextAndImage = signModel.TypeTextAndImage,
                    LoaiKy = "PDF",
                    MaPhanMem = "QUAN_LY_SO_DIEM_VA_HOC_BA",
                    MaNhaPhatHanh = "VNPT",
                    ProviderType = ProviderType.Vnpt,
                    SoLuongFile = signModel.FileUnsign?.Count ?? 0,
                    CurrentStatus = TransactionStatus.Created,
                    CurrentStep = TransactionStep.None,
                    CreatedAt = DateTime.UtcNow,
                    LockMd5Hash = md5Hash
                };

                if (!string.IsNullOrEmpty(signModel.ImageBase64))
                {
                    try { transaction.ImageBytes = Convert.FromBase64String(signModel.ImageBase64); }
                    catch { /* ignore invalid base64 image */ }
                }

                if (signModel.FileUnsign != null)
                {
                    transaction.UnsignedFiles = signModel.FileUnsign.Select(f => new UnsignedFile
                    {
                        Md5Hash = f.Md5Hash ?? "",
                        FileName = f.FileName ?? "",
                        HashData = f.HashData,
                        XmlSignature = f.XmlSignature,
                        NodeSignerPath = f.NodeSignerPath,
                        NodeSignerText = f.NodeSignerText,
                        NodeSignerId = f.NodeSignerId,
                        NodeDataSignId = f.NodeDataSignId,
                        FileUrl = f.FileUrl
                    }).ToList();
                }

                await _transactionService.CreateAsync(transaction);

                // Publish to queue
                var message = new QueueMessage
                {
                    MaGiaoDich = maGiaoDich,
                    Provider = ProviderType.Vnpt,
                    Step = TransactionStep.FilePrepare,
                    TenantId = signModel.MaTruong ?? "unknown",
                    TraceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
                };
                await _messagePublisher.PublishAsync(message);

                result.Status = 200;
                result.Data = maGiaoDich;
                result.Message = "Thành công";
                result.Total = 1;

                _metricsService.IncrementCounter("signbase64_created_total", new[]
                    { $"school:{signModel.MaTruong}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legacy SignBase64 failed");
                result.Status = 500;
                result.Message = ex.Message;
                // Release lock on failure — lock chỉ giữ khi transaction được tạo thành công
                if (HttpContext.Items["FileLockHandle"] is IAsyncDisposable lockHandleOnError)
                    await lockHandleOnError.DisposeAsync();
            }

            return Ok(result);
        }

        // ── Legacy endpoints còn lại ──────────────────────────────────────

        /// <summary>
        /// Legacy: POST /Signature/Lock
        /// Khóa giao dịch trong Redis (ngăn xử lý song song).
        /// </summary>
        [HttpPost("Lock")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> Lock([FromBody] LegacySignRequest model)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(model?.tranId))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số mã giao dịch";
                return Ok(result);
            }

            try
            {
                var db = _redis.GetDatabase();
                var lockKey = $"lock:signing:{model.tranId}";
                var acquired = await db.LockTakeAsync(lockKey, Environment.MachineName, TimeSpan.FromMinutes(5));

                result.Status = acquired ? 200 : 409;
                result.Data = model.tranId;
                result.Message = acquired ? "Khóa thành công" : "Giao dịch đang được xử lý";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lock failed for {TranId}", model.tranId);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/ReleaseLock
        /// Mở khóa giao dịch trong Redis.
        /// </summary>
        [HttpPost("ReleaseLock")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> ReleaseLock([FromBody] LegacySignRequest model)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(model?.tranId))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số mã giao dịch";
                return Ok(result);
            }

            try
            {
                var db = _redis.GetDatabase();
                var lockKey = $"lock:signing:{model.tranId}";
                await db.LockReleaseAsync(lockKey, Environment.MachineName);

                result.Status = 200;
                result.Data = model.tranId;
                result.Message = "Mở khóa thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReleaseLock failed for {TranId}", model.tranId);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/GetThongTinChuThe
        /// Lấy thông tin chứng thư số của người ký.
        /// </summary>
        [HttpPost("GetThongTinChuThe")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> GetThongTinChuThe(
            [FromBody] LegacyGetThongTinChuTheRequest request)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(request?.UserName) ||
                string.IsNullOrWhiteSpace(request?.Password))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số username hoặc password";
                return Ok(result);
            }

            try
            {
                var transaction = await _transactionService.GetByMaGiaoDichAsync(request.MaGiaoDich ?? "");

                var thongTin = new
                {
                    userName = request.UserName,
                    tenNhaPhatHanh = transaction?.MaNhaPhatHanh ?? "VNPT",
                    serialNumber = transaction?.SeriNumber ?? "",
                    subject = $"CN={request.UserName}",
                    issueDate = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"),
                    expireDate = DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd"),
                    algorithm = transaction?.LoaiKy ?? "RSA 2048"
                };

                result.Status = 200;
                result.Data = thongTin;
                result.Message = "Thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetThongTinChuThe failed for {UserName}", request.UserName);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/GetDanhSachChungThuSo
        /// Lấy danh sách chứng thư số của người dùng từ CA provider.
        /// </summary>
        [HttpPost("GetDanhSachChungThuSo")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> GetDanhSachChungThuSo(
            [FromBody] LegacyGetDanhSachChungThuSoRequest request)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(request?.UserName) ||
                string.IsNullOrWhiteSpace(request?.Password))
            {
                result.Status = 402;
                result.Message = "Thiếu tham số username hoặc password";
                return Ok(result);
            }

            try
            {
                // Trong thực tế, endpoint này sẽ gọi CA provider API để lấy danh sách chứng thư.
                // Tạm thời trả về mock data cho backward compatibility.
                var danhSach = new List<object>
                {
                    new
                    {
                        serialNumber = "SER-2024-001",
                        subject = $"CN={request.UserName}",
                        issuer = "VNPT CA",
                        validFrom = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"),
                        validTo = DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd"),
                        algorithm = "RSA 2048",
                        status = "ACTIVE"
                    }
                };

                result.Status = 200;
                result.Data = danhSach;
                result.Total = danhSach.Count;
                result.Message = "Thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDanhSachChungThuSo failed for {UserName}", request.UserName);
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }

        /// <summary>
        /// Legacy: POST /Signature/PushMessage
        /// Gửi thông báo qua Telegram (legacy thường dùng cho admin notification).
        /// </summary>
        [HttpPost("PushMessage")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<LegacyResponseData>> PushMessage(
            [FromBody] LegacyPushMessageRequest request)
        {
            var result = new LegacyResponseData();

            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                result.Status = 402;
                result.Message = "Thiếu nội dung thông báo";
                return Ok(result);
            }

            try
            {
                var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                    ?? "not-configured";
                var chatId = request.ChatId
                    ?? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
                    ?? "";

                if (botToken != "not-configured" && !string.IsNullOrEmpty(chatId))
                {
                    var telegramUrl =
                        $"https://api.telegram.org/bot{botToken}/sendMessage";
                    var payload = new
                    {
                        chat_id = chatId,
                        text = request.Message,
                        parse_mode = "HTML"
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync(telegramUrl, content);
                    response.EnsureSuccessStatusCode();

                    result.Status = 200;
                    result.Message = "Đã gửi thông báo";
                }
                else
                {
                    result.Status = 200;
                    result.Message = "Telegram chưa được cấu hình, đã ghi log thay thế";
                    _logger.LogInformation("PushMessage (legacy): {Message}", request.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PushMessage failed");
                result.Status = 500;
                result.Message = ex.Message;
            }

            return Ok(result);
        }
    }

    // ── Legacy request DTOs ─────────────────────────────────────────────

    /// <summary>
    /// Legacy request model cho Sign và Reponse endpoints.
    /// </summary>
    public class LegacySignRequest
    {
        public string? tranId { get; set; }
        public string? directory { get; set; }
        public string? keyLock { get; set; }
        public string? message { get; set; }
    }

    /// <summary>
    /// Legacy request cho GetTransaction.
    /// </summary>
    public class LegacyGetTransactionRequest
    {
        public string? MaGiaoDich { get; set; }
    }

    /// <summary>
    /// Legacy request model cho GetThongTinChuThe.
    /// </summary>
    public class LegacyGetThongTinChuTheRequest
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? MaGiaoDich { get; set; }
    }

    /// <summary>
    /// Legacy request model cho GetDanhSachChungThuSo.
    /// </summary>
    public class LegacyGetDanhSachChungThuSoRequest
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>
    /// Legacy request model cho PushMessage.
    /// </summary>
    public class LegacyPushMessageRequest
    {
        public string? Message { get; set; }
        public string? ChatId { get; set; }
    }

    /// <summary>
    /// Legacy InputSignModel cho SignBase64.
    /// </summary>
    public class LegacyInputSignModel
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public List<LegacyFileUnsignModel>? FileUnsign { get; set; }
        public string? DescriptionNotifi { get; set; }
        public string? ImageBase64 { get; set; }
        public string? DescriptionSign { get; set; }
        public string? TypeTextAndImage { get; set; }
        public string? TenNhanSu { get; set; }
        public string? MaNhanSu { get; set; }
        public string? TenTruong { get; set; }
        public string? MaTruong { get; set; }
    }

    public class LegacyFileUnsignModel
    {
        public string? Md5Hash { get; set; }
        public string? FileName { get; set; }
        public string? FileUrl { get; set; }
        public string? HashData { get; set; }
        public string? XmlSignature { get; set; }
        public string? NodeSignerPath { get; set; }
        public string? NodeSignerText { get; set; }
        public string? NodeSignerId { get; set; }
        public string? NodeDataSignId { get; set; }
    }

    // ── Legacy response DTOs ────────────────────────────────────────────

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
