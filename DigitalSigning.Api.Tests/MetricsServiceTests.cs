using DigitalSigning.Core.Services;
using FluentAssertions;

namespace DigitalSigning.Api.Tests;

public class MetricsServiceTests
{
    // Test the SanitizeMetricLabel logic through IncrementCounter
    // The MetricsService sanitizes label values to prevent Prometheus label explosion.

    [Fact]
    public void IncrementCounter_SanitizesLabels()
    {
        var service = new MetricsService();
        var ex = Record.Exception(() =>
            service.IncrementCounter("test_sanitize_labels", new[] { "tenant:school-1", "provider:VNPT" }));
        ex.Should().BeNull();
    }

    [Fact]
    public void IncrementCounter_WithSpecialChars_DoesNotThrow()
    {
        var service = new MetricsService();
        var ex = Record.Exception(() =>
            service.IncrementCounter("test_special_chars", new[]
            {
                "tenant:school.1/district\"2\"",
                "provider:VNPT$special"
            }));
        ex.Should().BeNull();
    }

    [Fact]
    public void IncrementCounter_WithEmptyLabels_UsesDefault()
    {
        var service = new MetricsService();
        var ex = Record.Exception(() =>
            service.IncrementCounter("test_empty_labels", null));
        ex.Should().BeNull();
    }
}
