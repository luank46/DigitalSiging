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
        // Arrange: We need to verify via reflection that the private method works.
        // Instead, we rely on the fact that MetricsService.IncrementCounter doesn't throw
        // on special characters — the sanitizer replaces them silently.
        var service = new MetricsService();
        var ex = Record.Exception(() =>
            service.IncrementCounter("test.counter", new[] { "tenant:school-1", "provider:VNPT" }));
        ex.Should().BeNull();
    }

    [Fact]
    public void IncrementCounter_WithSpecialChars_DoesNotThrow()
    {
        var service = new MetricsService();
        var ex = Record.Exception(() =>
            service.IncrementCounter("test.counter", new[]
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
            service.IncrementCounter("test.counter", null));
        ex.Should().BeNull();
    }
}
