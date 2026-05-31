using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Api.Models.Validators;
using DigitalSigning.Core.Enums;
using FluentAssertions;

namespace DigitalSigning.Api.Tests;

public class CreateTransactionRequestValidatorTests
{
    private readonly CreateTransactionRequestValidator _validator = new();

    private static CreateTransactionRequest ValidRequest() => new()
    {
        TenantId = "school-1",
        ProviderType = ProviderType.Vnpt,
        FileUrls = new List<string> { "https://example.com/file1.pdf" }
    };

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        var result = _validator.Validate(ValidRequest());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void MissingTenantId_FailsValidation()
    {
        var request = ValidRequest();
        request.TenantId = "";
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TenantId");
    }

    [Fact]
    public void InvalidProviderType_FailsValidation()
    {
        var request = ValidRequest();
        request.ProviderType = ProviderType.None;
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ProviderType");
    }

    [Fact]
    public void TooManyFileUrls_FailsValidation()
    {
        var request = ValidRequest();
        request.FileUrls = Enumerable.Range(0, 11)
            .Select(i => $"https://example.com/file{i}.pdf")
            .ToList();
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileUrls");
    }

    [Fact]
    public void EmptyFileUrls_FailsValidation()
    {
        var request = ValidRequest();
        request.FileUrls = new List<string>();
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InvalidUrlScheme_FailsValidation()
    {
        var request = ValidRequest();
        request.FileUrls = new List<string> { "ftp://example.com/file.pdf" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UrlExceedingMaxLength_FailsValidation()
    {
        var request = ValidRequest();
        request.FileUrls = new List<string> { "https://x.com/" + new string('a', 2100) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void WhitespaceUrl_FailsValidation()
    {
        var request = ValidRequest();
        request.FileUrls = new List<string> { "   " };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }
}
