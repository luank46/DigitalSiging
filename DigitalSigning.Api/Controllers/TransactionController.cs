using Microsoft.AspNetCore.Mvc;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Api.Models.Validators;
using FluentValidation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly ILogger<TransactionController> _logger;

    public TransactionController(
        ITransactionService transactionService,
        IMessagePublisher messagePublisher,
        IIdempotencyService idempotencyService,
        IMetricsService metricsService,
        ILogger<TransactionController> logger)
    {
        _transactionService = transactionService;
        _messagePublisher = messagePublisher;
        _idempotencyService = idempotencyService;
        _metricsService = metricsService;
        _logger = logger;
    }

    /// <summary>
    /// Tạo mới giao dịch ký số (endpoint chính của API).
    /// Giữ tương thích với endpoint cũ: POST /Signature/CreateTransaction
    /// </summary>
    /// <param name="request">Thông tin yêu cầu tạo giao dịch</param>
    /// <returns>Mã giao dịch (MaGiaoDich) nếu tạo thành công</returns>
    [HttpPost("CreateTransaction")]
    [ProducesResponseType(typeof(CreateTransactionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> CreateTransaction([FromBody] CreateTransactionRequest request)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        _logger.LogInformation("CreateTransaction request received for TenantId={TenantId}, Provider={Provider}",
            request.TenantId, request.ProviderType);

        try
        {
            // Validate request
            var validator = new CreateTransactionRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                _metricsService.IncrementCounter("api_validation_errors", new[] { "endpoint:CreateTransaction" });
                return BadRequest(new ApiErrorResponse
                {
                    ErrorCode = Core.Enums.ErrorCode.ValidationError.ToString(),
                    Message = "Validation failed",
                    Details = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)),
                    TraceId = traceId
                });
            }

            // Check idempotency (if key provided)
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var idempotencyResult = await _idempotencyService.CheckAndStoreAsync(
                    request.TenantId,
                    request.IdempotencyKey,
                    Guid.NewGuid().ToString("N")); // temporary txId

                if (idempotencyResult == Core.Enums.IdempotencyResult.Duplicate)
                {
                    _logger.LogInformation("Duplicate IdempotencyKey detected: {Key}", request.IdempotencyKey);
                    // Return existing transaction if we had a way to look it up by key
                    // For now, we'll proceed with new transaction (could be enhanced)
                }
            }

            // Create transaction document
            var transaction = new Transaction
            {
                MaGiaoDich = Guid.NewGuid().ToString("N"),
                TenantId = request.TenantId,
                ProviderType = request.ProviderType,
                CurrentStatus = TransactionStatus.Created,
                CurrentStep = TransactionStep.None,
                CreatedAt = DateTime.UtcNow,
                IsIdempotent = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            };

            await _transactionService.CreateAsync(transaction);

            // Record creation event
            await _transactionService.RecordEventAsync(
                transaction.MaGiaoDich,
                TransactionEventType.TransactionCreated,
                $"Transaction created with {request.FileUrls.Count} files");

            // Publish first step (FilePrepare)
            var firstMessage = new QueueMessage
            {
                MaGiaoDich = transaction.MaGiaoDich,
                TenantId = transaction.TenantId,
                Provider = transaction.ProviderType,
                Step = TransactionStep.FilePrepare,
                Payload = JsonSerializer.Serialize(request.FileUrls),
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
    /// </summary>
    [HttpPost("GetTransaction")]
    [ProducesResponseType(typeof(TransactionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> GetTransaction([FromBody] GetTransactionRequest request)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

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