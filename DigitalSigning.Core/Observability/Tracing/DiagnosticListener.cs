// See SigningDiagnostics.cs for the actual event emission helpers.
// This file is left as a namespace anchor for backward compatibility.

using System.Diagnostics;

namespace DigitalSigning.Core.Observability.Tracing;

/// <summary>
/// Provides access to a <see cref="System.Diagnostics.DiagnosticSource"/> instance
/// that can be consumed by OpenTelemetry listeners.
/// </summary>
public static class DiagnosticSourceProvider
{
    /// <summary>
    /// The single diagnostic source shared across the signing system.
    /// </summary>
    public static readonly DiagnosticSource Source = new DiagnosticListener("DigitalSigning");
}
