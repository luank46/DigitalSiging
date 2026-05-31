using System.Diagnostics;

namespace DigitalSigning.Core.Observability.Tracing;

/// <summary>
/// Defines shared ActivitySource names and helper methods for OpenTelemetry tracing.
/// Every component (API, Workers) uses the same source so all spans are linked
/// under one service hierarchy in the trace viewer.
/// </summary>
public static class ActivitySourceExtensions
{
    /// <summary>
    /// The single ActivitySource used by the entire DigitalSigning system.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(
        "DigitalSigning",
        "1.0.0");

    // ── Span / activity names ──────────────────────────────────────────
    public const string CreateTransaction = "DigitalSigning.Transaction.Create";
    public const string SignDocument      = "DigitalSigning.Transaction.Sign";
    public const string FilePrepare       = "DigitalSigning.Worker.FilePrepare";
    public const string Hash              = "DigitalSigning.Worker.Hash";
    public const string ProviderRequest   = "DigitalSigning.Worker.ProviderRequest";
    public const string WaitingConfirm    = "DigitalSigning.Worker.WaitingConfirm";
    public const string AppendSignature   = "DigitalSigning.Worker.AppendSignature";
    public const string Upload            = "DigitalSigning.Worker.Upload";
    public const string WebhookNotify     = "DigitalSigning.Worker.WebhookNotify";
    public const string MongoDbCall       = "DigitalSigning.MongoDB";
    public const string RabbitMqPublish   = "DigitalSigning.RabbitMQ.Publish";
    public const string RabbitMqConsume   = "DigitalSigning.RabbitMQ.Consume";
    public const string RedisCall         = "DigitalSigning.Redis";

    // ── Tag keys (semantic convention aligned) ─────────────────────────
    public const string TagMaGiaoDich    = "signing.ma_giao_dich";
    public const string TagTenantId      = "signing.tenant_id";
    public const string TagProvider      = "signing.provider";
    public const string TagStep          = "signing.step";
    public const string TagAttempt       = "signing.attempt";
    public const string TagErrorCode     = "signing.error_code";
    public const string TagQueueName     = "messaging.queue";
    public const string TagMessageId     = "messaging.message_id";

    /// <summary>
    /// Convenience wrapper to start an activity with preset tags.
    /// </summary>
    public static Activity? StartActivity(string name, string maGiaoDich, string tenantId)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
        activity?.SetTag(TagMaGiaoDich, maGiaoDich);
        activity?.SetTag(TagTenantId, tenantId);
        activity?.SetTag("service.name", "DigitalSigning");
        return activity;
    }

    /// <summary>
    /// Creates a child activity that inherits the parent's trace context.
    /// </summary>
    public static Activity? StartChildActivity(this Activity? parent, string name)
    {
        if (parent == null) return null;
        return ActivitySource.StartActivity(name, ActivityKind.Internal);
    }
}
