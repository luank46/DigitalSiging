using Microsoft.AspNetCore.Mvc;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Api.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Api.Controllers;

/// <summary>
/// API endpoints for managing transactions: cancel, retry, query status.
/// </summary>
[ApiController]
[Route("Signature/Transactions")]
public class ManagementController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IMessagePublisher _messagePublisher;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<ManagementController> _logger;

    public ManagementController(
        ITransactionService transactionService,
        IMessagePublisher messagePublisher,
        IMetricsService metricsService,
        ILogger<ManagementController> logger)
    {
        _transactionService = transactionService;
        _messagePublisher = messagePublisher;
        _metricsService = metricsService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy trạng thái giao dịch chi tiết.
    /// Endpoint mới: GET /Signature/Transactions/{maGiaoDich}/status
    /// </summary>
    [HttpGet("{maGiaoDich}/status")]
    [ProducesResponseType(typeof(TransactionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> GetStatus(string maGiaoDich)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(maGiaoDich))
        {
            return BadRequest(new ApiErrorResponse
            {
                ErrorCode = ErrorCode.ValidationError.ToString(),
                Message = "MaGiaoDich is required",
                TraceId = traceId
            });
        }

        try
        {
            var transaction = await _transactionService.GetByMaGiaoDichAsync(maGiaoDich);
            if (transaction == null)
            {
                return NotFound(new ApiErrorResponse
                {
                    ErrorCode = ErrorCode.NotFound.ToString(),
                    Message = $"Transaction not found: {maGiaoDich}",
                    TraceId = traceId
                });
            }

            return Ok(new TransactionResponse
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
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status for {MaGiaoDich}", maGiaoDich);
            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to retrieve transaction status",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }

    /// <summary>
    /// Hủy giao dịch đang xử lý.
    /// Endpoint mới: POST /Signature/Transactions/{maGiaoDich}/cancel
    /// </summary>
    [HttpPost("{maGiaoDich}/cancel")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> Cancel(string maGiaoDich)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(maGiaoDich))
        {
            return BadRequest(new ApiErrorResponse
            {
                ErrorCode = ErrorCode.ValidationError.ToString(),
                Message = "MaGiaoDich is required",
                TraceId = traceId
            });
        }

        try
        {
            var transaction = await _transactionService.GetByMaGiaoDichAsync(maGiaoDich);
            if (transaction == null)
            {
                return NotFound(new ApiErrorResponse
                {
                    ErrorCode = ErrorCode.NotFound.ToString(),
                    Message = $"Transaction not found: {maGiaoDich}",
                    TraceId = traceId
                });
            }

            // Can only cancel if in non-terminal state
            if (transaction.CurrentStatus == TransactionStatus.Completed ||
                transaction.CurrentStatus == TransactionStatus.Cancelled)
            {
                return BadRequest(new ApiErrorResponse
                {
                    ErrorCode = ErrorCode.ValidationError.ToString(),
                    Message = $"Cannot cancel transaction in status: {transaction.CurrentStatus}",
                    TraceId = traceId
                });
            }

            await _transactionService.UpdateStatusAsync(maGiaoDich, TransactionStatus.Cancelled, TransactionStep.None);
            await _transactionService.RecordEventAsync(maGiaoDich, TransactionEventType.StatusUpdated, "Transaction cancelled by user");

            _metricsService.IncrementCounter("transactions_cancelled_total", new[] { $"provider:{transaction.ProviderType}" });
            _logger.LogInformation("Transaction {MaGiaoDich} cancelled", maGiaoDich);

            return Ok(new
            {
                maGiaoDich = transaction.MaGiaoDich,
                currentStatus = TransactionStatus.Cancelled.ToString(),
                message = "Transaction cancelled successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling transaction {MaGiaoDich}", maGiaoDich);
            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to cancel transaction",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }

    /// <summary>
    /// Thử lại giao dịch bị lỗi hoặc timeout.
    /// Endpoint mới: POST /Signature/Transactions/{maGiaoDich}/retry
    /// </summary>
    [HttpPost("{maGiaoDich}/retry")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> Retry(string maGiaoDich)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(maGiaoDich))
        {
            return BadRequest(new ApiErrorResponse
            {
                ErrorCode = ErrorCode.ValidationError.ToString(),
                Message = "MaGiaoDich is required",
                TraceId = traceId
            });
        }

        try
        {
            var transaction = await _transactionService.GetByMaGiaoDichAsync(maGiaoDich);
            if (transaction == null)
            {
                return NotFound(new ApiErrorResponse
                {
                    ErrorCode = ErrorCode.NotFound.ToString(),
                    Message = $"Transaction not found: {maGiaoDich}",
                    TraceId = traceId
                });
            }

            // Can only retry failed or timed out transactions
            if (transaction.CurrentStatus != TransactionStatus.Failed &&
                transaction.CurrentStatus != TransactionStatus.Timeout)
            {
                return BadRequest(new ApiErrorResponse
                {
                    ErrorCode = ErrorCode.ValidationError.ToString(),
                    Message = $"Cannot retry transaction in status: {transaction.CurrentStatus}",
                    TraceId = traceId
                });
            }

            // Reset to Queued and publish FilePrepare message to restart the pipeline
            await _transactionService.UpdateStatusAsync(maGiaoDich, TransactionStatus.Queued, TransactionStep.FilePrepare);
            await _transactionService.RecordEventAsync(maGiaoDich, TransactionEventType.StatusUpdated, "Transaction queued for retry");

            var retryMessage = new QueueMessage
            {
                MaGiaoDich = maGiaoDich,
                TenantId = transaction.TenantId,
                Provider = transaction.ProviderType,
                Step = TransactionStep.FilePrepare,
                TraceId = traceId,
                Attempt = transaction.RetryCount + 1
            };

            await _messagePublisher.PublishAsync(retryMessage);

            _metricsService.IncrementCounter("transactions_retried_total", new[] { $"provider:{transaction.ProviderType}" });
            _logger.LogInformation("Transaction {MaGiaoDich} queued for retry (attempt {Attempt})",
                maGiaoDich, transaction.RetryCount + 1);

            return Ok(new
            {
                maGiaoDich = transaction.MaGiaoDich,
                currentStatus = TransactionStatus.Queued.ToString(),
                message = "Transaction queued for retry"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying transaction {MaGiaoDich}", maGiaoDich);
            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to retry transaction",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }
}