using FluentValidation;
using DigitalSigning.Api.Models.DTOs;

namespace DigitalSigning.Api.Models.Validators;

public class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required")
            .MaximumLength(100);

        RuleFor(x => x.ProviderType)
            .IsInEnum().WithMessage("ProviderType must be a valid value (VNPT=1, Viettel=2, BKAV=3, MISA=4, GCC=5)")
            .NotEqual(DigitalSigning.Core.Enums.ProviderType.None).WithMessage("ProviderType must not be None");

        RuleFor(x => x.FileUrls)
            .NotEmpty().WithMessage("At least one file URL is required")
            .Must(urls => urls.Count <= 10).WithMessage("Maximum 10 file URLs are allowed")
            .Must(urls => urls.All(u => !string.IsNullOrWhiteSpace(u)))
                .WithMessage("Each file URL must not be empty or whitespace")
            .Must(urls => urls.All(u => u.Trim().Length <= 2048))
                .WithMessage("Each file URL must not exceed 2048 characters")
            .Must(urls => urls.Select(u => u.Trim()).All(u =>
                Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            .WithMessage("Each file URL must be a valid HTTP or HTTPS URL");

        RuleFor(x => x.WebhookUrl)
            .Must(u => string.IsNullOrWhiteSpace(u) || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("WebhookUrl must be a valid URL if provided");

        // Validate email only when SignerInfo.Email is present
        When(x => x.SignerInfo != null && !string.IsNullOrWhiteSpace(x.SignerInfo.Email), () =>
        {
            RuleFor(x => x.SignerInfo!.Email)
                .EmailAddress()
                .WithMessage("Invalid email format");
        });
    }
}
