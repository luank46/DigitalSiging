using Microsoft.AspNetCore.Mvc;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using DigitalSigning.Api.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Api.Controllers;

/// <summary>
/// API endpoints for retrieving transaction event history.
/// </summary>
[ApiController]
[Route("Signature")]
public class EventController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ITransactionEventRepository _eventRepository;
    private readonly ILogger<EventController> _logger;

    public EventController(
        ITransactionService transactionService,
        ITransactionEventRepository eventRepository,
        ILogger<EventController> logger)
    {
        _transactionService = transactionService;
        _eventRepository = eventRepository;
        _logger = logger;
    }

    /// <summary>
    /// Lấy lịch sử sự kiện của giao dịch.
    /// Endpoint mới: POST /Signature/Transactions/{maGiaoDich}/events
    /// </summary>
    [HttpPost("Transactions/{maGiaoDich}/events")]
    [ProducesResponseType(typeof(List<TransactionEventResponse>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 500)]
    public async Task<IActionResult> GetTransactionEvents(string maGiaoDich)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        _logger.LogInformation("GetTransactionEvents request for {MaGiaoDich}", maGiaoDich);

        if (string.IsNullOrWhiteSpace(maGiaoDich))
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
            // Verify transaction exists
            var transaction = await _transactionService.GetByMaGiaoDichAsync(maGiaoDich);
            if (transaction == null)
            {
                return NotFound(new ApiErrorResponse
                {
                    ErrorCode = Core.Enums.ErrorCode.NotFound.ToString(),
                    Message = $"Transaction not found: {maGiaoDich}",
                    TraceId = traceId
                });
            }

            // Load real events from MongoDB
            var rawEvents = await _eventRepository.GetByMaGiaoDichAsync(maGiaoDich);

            var events = rawEvents.Select(e => new TransactionEventResponse
            {
                MaGiaoDich = e.MaGiaoDich,
                EventType = e.EventType,
                Status = e.Status,
                Details = e.Details,
                CreatedAt = e.CreatedAt,
                ActionData = e.ActionData != null ? new ActionDataResponse
                {
                    ActionType = e.ActionData.ActionType,
                    Details = e.ActionData.Details
                } : null
            }).ToList();

            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction events for {MaGiaoDich}", maGiaoDich);
            return StatusCode(500, new ApiErrorResponse
            {
                ErrorCode = Core.Enums.ErrorCode.UnexpectedException.ToString(),
                Message = "Failed to retrieve transaction events",
                Details = _logger.IsEnabled(LogLevel.Debug) ? ex.Message : null,
                TraceId = traceId
            });
        }
    }
}
