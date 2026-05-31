using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Observability.Tracing;

/// <summary>
/// Helper that emits diagnostic events at key points in the signing pipeline.
/// These events can be consumed by listeners (e.g. OpenTelemetry, diagnostic tools)
/// without coupling the domain code to a specific telemetry backend.
/// </summary>
public static class SigningDiagnostics
{
    // ── Event names ───────────────────────────────────────────────────

    public const string TransactionCreated    = "Transaction.Created";
    public const string TransactionCompleted  = "Transaction.Completed";
    public const string TransactionFailed     = "Transaction.Failed";
    public const string MessagePublished      = "Message.Published";
    public const string MessageConsumed       = "Message.Consumed";
    public const string MessageSentToDlq      = "Message.SentToDlq";
    public const string RateLimitExceeded     = "RateLimit.Exceeded";
    public const string CircuitBreakerTripped = "CircuitBreaker.Tripped";
    public const string WaitingStateEntered   = "Waiting.StateEntered";
    public const string WaitingStateResolved  = "Waiting.StateResolved";

    // ── Event payload types ───────────────────────────────────────────

    public record TransactionEventPayload(
        string MaGiaoDich,
        string TenantId,
        string Provider,
        string Status,
        string Step,
        int Attempt,
        long ElapsedMs);

    public record MessagePayload(
        string MessageId,
        string MaGiaoDich,
        string Queue,
        string Step);

    public record DlqPayload(
        string OriginalId,
        string MaGiaoDich,
        string FailedStep,
        string ErrorCode,
        string ErrorMessage);

    public record ProtectionPayload(
        string Key,
        string Policy,
        bool Allowed);

    /// <summary>
    /// Fires transaction-related events with a structured payload.
    /// </summary>
    public static void EmitTransactionEvent(string eventName, TransactionEventPayload payload)
    {
        // Placeholder for future event bus / listener integration
        // In production, this could publish to an internal IObservable or event stream.
    }

    /// <summary>
    /// Fires message-related events.
    /// </summary>
    public static void EmitMessageEvent(string eventName, MessagePayload payload)
    {
    }

    /// <summary>
    /// Fires a dead-letter queue event.
    /// </summary>
    public static void EmitDlqEvent(DlqPayload payload)
    {
    }

    /// <summary>
    /// Fires protection-related events (rate limit, circuit breaker).
    /// </summary>
    public static void EmitProtectionEvent(string eventName, ProtectionPayload payload)
    {
    }

    /// <summary>
    /// Fires events automatically from a <see cref="Transaction"/> instance.
    /// </summary>
    public static void EmitFromTransaction(Transaction tx, string eventName, long? elapsedMs = null)
    {
        EmitTransactionEvent(eventName, new TransactionEventPayload(
            tx.MaGiaoDich,
            tx.TenantId,
            tx.ProviderType.ToString(),
            tx.CurrentStatus.ToString(),
            tx.CurrentStep.ToString(),
            tx.RetryCount,
            elapsedMs ?? 0));
    }
}
