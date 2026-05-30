using System.Text.Json.Serialization;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Api.Models.DTOs;

/// <summary>
/// Response DTO for transaction status information.
/// </summary>
public class TransactionResponse
{
    [JsonPropertyName("maGiaoDich")]
    public string MaGiaoDich { get; set; } = string.Empty;

    [JsonPropertyName("tenentId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("providerType")]
    public ProviderType ProviderType { get; set; }

    [JsonPropertyName("currentStatus")]
    public TransactionStatus CurrentStatus { get; set; }

    [JsonPropertyName("currentStep")]
    public TransactionStep CurrentStep { get; set; }

    [JsonPropertyName("errorCode")]
    public ErrorCode? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("signedFiles")]
    public List<SignedFileInfo>? SignedFiles { get; set; }
}

/// <summary>
/// Thông tin file đã ký.
/// </summary>
public class SignedFileInfo
{
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("md5Hash")]
    public string? Md5Hash { get; set; }

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}

/// <summary>
/// Response khi tạo giao dịch thành công.
/// </summary>
public class CreateTransactionResponse
{
    [JsonPropertyName("maGiaoDich")]
    public string MaGiaoDich { get; set; } = string.Empty;

    [JsonPropertyName("currentStatus")]
    public TransactionStatus CurrentStatus { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Transaction created successfully";
}

/// <summary>
/// Response cho event history.
/// </summary>
public class TransactionEventResponse
{
    [JsonPropertyName("maGiaoDich")]
    public string MaGiaoDich { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public TransactionEventType EventType { get; set; }

    [JsonPropertyName("status")]
    public EventStatus Status { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("actionData")]
    public ActionDataResponse? ActionData { get; set; }
}

public class ActionDataResponse
{
    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

/// <summary>
/// Response cho webhook delivery history.
/// </summary>
public class WebhookDeliveryResponse
{
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("attemptedAt")]
    public DateTime AttemptedAt { get; set; }

    [JsonPropertyName("attemptNumber")]
    public int AttemptNumber { get; set; }
}

/// <summary>
/// Standard API error response.
/// </summary>
public class ApiErrorResponse
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
}
