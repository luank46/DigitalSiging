using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace DigitalSigning.Core.Observability.Logging;

/// <summary>
/// Enriches every log event with the current <see cref="Activity"/>'s TraceId and SpanId.
/// This connects structured logs with OpenTelemetry traces so you can correlate
/// "what happened" (log) with "what was happening" (trace) in a single view.
/// </summary>
public class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        // TraceId links logs across services; SpanId links logs within one service.
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "SpanId", activity.SpanId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ParentSpanId", activity.ParentSpanId.ToString()));

        // If the activity was started via ActivitySourceExtensions, carry the
        // signing-specific tags as log properties too.
        if (activity.Tags.Any())
        {
            var signingTags = activity.Tags
                .Where(t => t.Key?.StartsWith("signing.") == true)
                .Select(t => new LogEventProperty(t.Key ?? "unknown",
                    new ScalarValue(t.Value)));

            foreach (var prop in signingTags)
                logEvent.AddPropertyIfAbsent(prop);
        }
    }
}
