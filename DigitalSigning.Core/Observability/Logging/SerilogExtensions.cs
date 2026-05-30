using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace DigitalSigning.Core.Observability.Logging;

/// <summary>
/// Extension methods for configuring Serilog in the DigitalSigning system.
///
/// Usage:
///   builder.Host.UseSerilog((ctx, cfg) => cfg
///       .ReadFrom.Configuration(ctx.Configuration)
///       .UseDigitalSigningEnrichers());
/// </summary>
public static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog with sensible defaults for the signing system:
    /// console sink + structured enrichers.
    /// Call this from <c>Program.cs</c> after <c>CreateBuilder</c>.
    /// </summary>
    public static IHostBuilder UseDigitalSigningSerilog(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, services, configuration) =>
        {
            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.With<ActivityEnricher>()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .WriteTo.Console(
                    outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {TraceId} {SpanId}{NewLine}{Exception}");

            // Apply config-specified overrides (if any) on top of defaults.
            configuration.ReadFrom.Configuration(context.Configuration);
        });

        return hostBuilder;
    }
}
