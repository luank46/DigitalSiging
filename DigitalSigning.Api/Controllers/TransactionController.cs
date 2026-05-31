using Microsoft.AspNetCore.Mvc;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Api.Models.Validators;
using DigitalSigning.Core.Protection;
using FluentValidation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http; // for RequestSizeLimit attribute

namespace DigitalSigning.Api.Controllers;

/// <summary>
/// API endpoints for managing signature transactions.
/// </summary>
[ApiController]
[Route("Signature")]
public class TransactionController : ControllerBase
{
	private readonly ITransactionService _transactionService;
	private readonly IMessagePublisher _messagePublisher;
	private readonly IIdempotencyService _idempotencyService;
	private readonly IMetricsService _metricsService;
	private readonly IRateLimiter _rateLimiter;
	private readonly Core.Settings.FeatureFlags _featureFlags;
	private readonly FileLockService _fileLock;
	private readonly ILogger<TransactionController> _logger;

	public TransactionController(
		ITransactionService transactionService,
		IMessagePublisher messagePublisher,
		IIdempotencyService idempotencyService,
		IMetricsService metricsService,
		IRateLimiter rateLimiter,
		Core.Settings.FeatureFlags featureFlags,
		FileLockService fileLock,
		ILogger<TransactionController> logger)
	{
		_transactionService = transactionService;
		_messagePublisher = messagePublisher;
		_idempotencyService = idempotencyService;
		_metricsService = metricsService;
		_rateLimiter = rateLimiter;
		_featureFlags = featureFlags;
		_fileLock = fileLock;
		_logger = logger;
	}

    /// <summary>
    /// Tạo mới giao dịch ký số (endpoint chính của API).
    /// Giữ tương thích với endpoint cũ: POST /Signature/CreateTransaction
    ///
    /// <list type="bullet">
    ///   <item><description>Kiểm tra idempotency: nếu <c>IdempotencyKey</c> được cung cấp và đã tồn tại,
    ///       trả về giao dịch cũ (HTTP 200) thay vì tạo mới.</description></item>
    ///   <item><description>Rate‑limiting: mỗi tenant/provider bị giới hạn số request/giây.
    ///       Khi vượt quá, trả về 429 Too Many Requests.</description></item>
    ///   <item><description>Kích thước request tối đa: 512 KB.
    ///       Danh sách file URL tối đa 10 URL, mỗi URL ≤ 2.048 ký tự.</description></item>
    /// </list>
    /// </summary>
    /// <param name="request">Thông tin yêu cầu tạo giao dịch</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Mã giao dịch (MaGiaoDich) nếu tạo thành công</returns>
    /// <response code="200">Tạo giao dịch thành công hoặc trả về giao dịch cũ do idempotency</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ (validation error)</response>
    /// <response code="429">Vượt quá rate limit, thử lại sau</response>
    /// <response code="499">Request bị hủy bởi client</response>
    /// <response code="500">Lỗi server không xác định</response>
    [HttpPost("CreateTransaction")]
    [RequestSizeLimit(524_288)] // 512 KB
    [ProducesResponseType(typeof(CreateTransactionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> CreateTransaction(
        [FromBody] CreateTransactionRequest request,
        CancellationToken ct = default)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        // Push traceId into the logging scope so all subsequent log entries carry it
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["TenantId"] = request.TenantId,
            ["ProviderType"] = request.ProviderType
        });

        _logger.LogInformation("CreateTransaction request received");

        // Rate-limit check (per-provider and per-tenant)
        if (!_rateLimiter.TryAcquire(request.ProviderType, request.TenantId))
        {
            _logger.LogWarning("Rate limit exceeded for TenantId={TenantId}, Provider={Provider}",
                request.TenantId, request.ProviderType);
            _metricsService.IncrementCounter("api_rate_limited_total", new[]
            {
                $"tenant:{request.TenantId}",
                $"provider:{request.ProviderType}"
            });
            return StatusCode(429, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.ValidationError.ToString(),
                Message = "Too many requests. Please try again later.",
                TraceId = traceId
            });
        }

        try
        {
            // Validate request
            var validator = new CreateTransactionRequestValidator();
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                _metricsService.IncrementCounter("api_validation_errors", new[] { "endpoint:CreateTransaction" });
                var errorDetails = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                return BadRequest(new ApiErrorResponse
                {
                    ErrorCode = Core.Enums.ErrorCode.ValidationError.ToString(),
                    Message = "Validation failed",
                    Details = errorDetails,
                    TraceId = traceId
                });
            }

            // File-level lock: check BEFORE idempotency (fail fast)
            var lockedFiles = new List<string>();
            foreach (var fileUrl in request.FileUrls)
            {
                var urlHash = Core.Services.FileLockService.ComputeUrlHashStatic(fileUrl.Trim());
                if (await _fileLock.IsLockedAsync(urlHash))
                {
                    lockedFiles.Add(fileUrl);
                }
            }

            if (lockedFiles.Count > 0)
            {
                _logger.LogWarning(
                    "File lock conflict for {LockedCount} of {TotalCount} files. Locked URLs: {Locked}",
                    lockedFiles.Count, request.FileUrls.Count, string.Join("; ", lockedFiles));

                _metricsService.IncrementCounter("api_file_lock_conflicts_total", new[]
                {
                    $"tenant:{request.TenantId}"
                });

                return StatusCode(409, new ApiErrorResponse
                {
                    ErrorCode = Core.Enums.ErrorCode.DuplicateRequest.ToString(),
                    Message = "Một hoặc nhiều file đang được ký bởi giao dịch khác. Vui lòng thử lại sau.",
                    Details = $"Số file bị khóa: {lockedFiles.Count}/{request.FileUrls.Count}",
                    TraceId = traceId
                });
            }

            // Check idempotency (if key provided)
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                if (_featureFlags.IdempotencyV2)
                {
                    // V2: lookup existing transaction by key, return if found
                    var existingKey = await _idempotencyService.GetByIdempotencyKeyAsync(
                        request.TenantId, request.IdempotencyKey);

                    if (existingKey != null)
                    {
                        _logger.LogInformation(
                            "Duplicate IdempotencyKey detected: {Key}. Returning existing transaction {MaGiaoDich}",
                            request.IdempotencyKey, existingKey.TransactionId);

                        var existingTx = await _transactionService.GetByMaGiaoDichAsync(existingKey.TransactionId);
                        if (existingTx != null)
                        {
                            return Ok(new CreateTransactionResponse
                            {
                                MaGiaoDich = existingTx.MaGiaoDich,
                                CurrentStatus = existingTx.CurrentStatus
                            });
                        }

                        _logger.LogWarning(
                            "Idempotency key {Key} references non-existent transaction {TxId}. Creating new.",
                            request.IdempotencyKey, existingKey.TransactionId);
                    }
                }
                else
                {
                    // V1 (fallback): original behavior — just log duplicate
                    var result = await _idempotencyService.CheckAndStoreAsync(
                        request.TenantId,
                        request.IdempotencyKey,
                        Guid.NewGuid().ToString("N"));

                    if (result == IdempotencyResult.Duplicate)
                    {
                        _logger.LogInformation("Duplicate IdempotencyKey detected (V1 fallback): {Key}",
                            request.IdempotencyKey);
                    }
                }
            }

            // Acquire lock for all files, auto-release on exception
            var lockHandles = new List<IAsyncDisposable>(request.FileUrls.Count);

            // Create transaction document
            var maGiaoDich = Guid.NewGuid().ToString("N");

                foreach (var fileUrl in request.FileUrls)
                {
                    var lockHandle = await _fileLock.AcquireByUrlAsync(
                        fileUrl.Trim(),
                        $"{request.TenantId}:{maGiaoDich}",
                        TimeSpan.FromMinutes(10), ct);

                    if (lockHandle != null)
                        lockHandles.Add(lockHandle);
                }

            var transaction = new Transaction
            {
                MaGiaoDich = maGiaoDich,
                TenantId = request.TenantId,
                ProviderType = request.ProviderType,
                CurrentStatus = TransactionStatus.Created,
                CurrentStep = TransactionStep.None,
                CreatedAt = DateTime.UtcNow,
                IsIdempotent = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            };

            // Persist idempotency key BEFORE creating the transaction (to prevent race conditions)
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var storeResult = await _idempotencyService.CheckAndStoreAsync(
                    request.TenantId,
                    request.IdempotencyKey,
                    maGiaoDich);

                if (storeResult == IdempotencyResult.Duplicate)
                {
                    // Race condition: another request stored the key between our lookup and store
                    var winningKey = await _idempotencyService.GetByIdempotencyKeyAsync(
                        request.TenantId, request.IdempotencyKey);
                    if (winningKey != null)
                    {
                        var winningTx = await _transactionService.GetByMaGiaoDichAsync(winningKey.TransactionId);
                        if (winningTx != null)
                        {
                            return Ok(new CreateTransactionResponse
                            {
                                MaGiaoDich = winningTx.MaGiaoDich,
                                CurrentStatus = winningTx.CurrentStatus
                            });
                        }
                    }
                }
            }

            await _transactionService.CreateAsync(transaction, ct);

            // Record creation event
            await _transactionService.RecordEventAsync(
                transaction.MaGiaoDich,
                TransactionEventType.TransactionCreated,
                $"Transaction created with {request.FileUrls.Count} files");

            // Publish first step (FilePrepare) — trim whitespace from URLs
            var trimmedUrls = request.FileUrls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .ToList();
            var firstMessage = new QueueMessage
            {
                MaGiaoDich = transaction.MaGiaoDich,
                TenantId = transaction.TenantId,
                Provider = transaction.ProviderType,
                Step = TransactionStep.FilePrepare,
                Payload = JsonSerializer.Serialize(trimmedUrls),
                TraceId = traceId
            };

            await _messagePublisher.PublishAsync(firstMessage);

            _logger.LogInformation("Transaction created successfully: {MaGiaoDich}", transaction.MaGiaoDich);
            _metricsService.IncrementCounter("transactions_created_total", new[] {
                $"tenant:{request.TenantId}",
                $"provider:{request.ProviderType}"
            });

            return Ok(new CreateTransactionResponse
            {
                MaGiaoDich = transaction.MaGiaoDich,
                CurrentStatus = transaction.CurrentStatus
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CreateTransaction cancelled for TenantId={TenantId}", request.TenantId);
            return StatusCode(499, new ApiErrorResponse  // 499 = Client Closed Request
            {
                ErrorCode = Core.Enums.ErrorCode.Timeout.ToString(),
                Message = "Request was cancelled by the client",
                TraceId = traceId
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout creating transaction for TenantId={TenantId}", request.TenantId);
            _metricsService.IncrementCounter("api_errors_total", new[] { "endpoint:CreateTransaction" });
            return StatusCode(503, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.Timeout.ToString(),
                Message = "Downstream service timed out while creating transaction",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation creating transaction for TenantId={TenantId}", request.TenantId);
            _metricsService.IncrementCounter("api_errors_total", new[] { "endpoint:CreateTransaction" });
            return StatusCode(409, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.ValidationError.ToString(),
                Message = "Cannot create transaction due to business rule violation",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating transaction for TenantId={TenantId}", request.TenantId);
            _metricsService.IncrementCounter("api_errors_total", new[] { "endpoint:CreateTransaction" });

            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to create transaction",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }

    /// <summary>
    /// Lấy trạng thái giao dịch (legacy compatibility).
    /// Giữ tương thích với endpoint cũ: POST /Signature/GetTransaction
    ///
    /// Kích thước request tối đa: 10 KB.
    /// </summary>
    [HttpPost("GetTransaction")]
    [RequestSizeLimit(10_240)] // 10 KB (just a transaction ID)
    [ProducesResponseType(typeof(TransactionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> GetTransaction([FromBody] GetTransactionRequest request)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["MaGiaoDich"] = request?.MaGiaoDich ?? "unknown"
        });

        if (string.IsNullOrWhiteSpace(request?.MaGiaoDich))
        {
            return BadRequest(new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.ValidationError.ToString(),
                Message = "MaGiaoDich is required",
                TraceId = traceId
            });
        }

        try
        {
            var transaction = await _transactionService.GetByMaGiaoDichAsync(request.MaGiaoDich);

            if (transaction == null)
            {
                _metricsService.IncrementCounter("api_not_found_total", new[] { "endpoint:GetTransaction" });
                return NotFound(new ApiErrorResponse
                {
                    ErrorCode = Core.Enums.ErrorCode.NotFound.ToString(),
                    Message = $"Transaction not found: {request.MaGiaoDich}",
                    TraceId = traceId
                });
            }

            var response = new TransactionResponse
            {
                MaGiaoDich = transaction.MaGiaoDich,
                TenantId = transaction.TenantId,
                ProviderType = transaction.ProviderType,
                CurrentStatus = transaction.CurrentStatus,
                CurrentStep = transaction.CurrentStep,
                ErrorCode = transaction.ErrorCode,
                ErrorMessage = transaction.ErrorMessage,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt,
                CompletedAt = transaction.DigitalSignature?.SignatureTimestamp,
                SignedFiles = transaction.DigitalSignature != null ?
                    new List<SignedFileInfo> {
                        new SignedFileInfo {
                            FileName = "signed_file", // Simplified - in real implementation would list actual files
                            Md5Hash = transaction.Checksum?.FileHash,
                            Signature = transaction.DigitalSignature?.SignatureValue
                        }
                    } : null
            };

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GetTransaction cancelled for {MaGiaoDich}", request?.MaGiaoDich);
            return StatusCode(499, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.Timeout.ToString(),
                Message = "Request was cancelled by the client",
                TraceId = traceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction {MaGiaoDich}", request?.MaGiaoDich);
            _metricsService.IncrementCounter("api_errors_total", new[] { "endpoint:GetTransaction" });

            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to retrieve transaction",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }
}

/// <summary>
/// Request DTO for getting transaction status.
/// </summary>
public class GetTransactionRequest
{
    [JsonPropertyName("maGiaoDich")]
    public string MaGiaoDich { get; set; } = string.Empty;
}