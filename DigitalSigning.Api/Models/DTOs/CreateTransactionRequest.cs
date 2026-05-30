using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DigitalSigning.Core.Enums;

namespace DigitalSigning.Api.Models.DTOs;

/// <summary>
/// Request DTO for creating a new signature transaction.
/// </summary>
public class CreateTransactionRequest
{
    /// <summary>Mã định danh tenant (trường học, đơn vị).</summary>
    [Required(ErrorMessage = "TenantId is required")]
    [StringLength(100, MinimumLength = 1)]
    [JsonPropertyName("tenentId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Loại nhà cung cấp CA (VNPT, Viettel, BKAV, MISA, GCC).</summary>
    [Required(ErrorMessage = "ProviderType is required")]
    [JsonPropertyName("providerType")]
    public ProviderType ProviderType { get; set; }

    /// <summary>Danh sách URL file cần ký.</summary>
    [Required(ErrorMessage = "At least one file URL is required")]
    [MinLength(1, ErrorMessage = "At least one file URL is required")]
    [JsonPropertyName("fileUrls")]
    public List<string> FileUrls { get; set; } = new();

    /// <summary>Thông tin người ký (tùy chọn).</summary>
    [JsonPropertyName("signerInfo")]
    public SignerInfoDto? SignerInfo { get; set; }

    /// <summary>Webhook URL để nhận kết quả ký (tùy chọn).</summary>
    [Url(ErrorMessage = "WebhookUrl must be a valid URL")]
    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    /// <summary>Idempotency key – nếu client retry cùng key, trả kết quả cũ.</summary>
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Thông tin người ký trong request.
/// </summary>
public class SignerInfoDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("studentCode")]
    public string? StudentCode { get; set; }

    [JsonPropertyName("email")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    [Phone(ErrorMessage = "Invalid phone format")]
    public string? Phone { get; set; }
}
