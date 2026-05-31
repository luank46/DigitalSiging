using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.WorkerBase
{
    /// <summary>
    /// Abstract base class for all worker services.
    /// Features:
    ///   - Retry with exponential backoff + DLQ routing
    ///   - Auto reconnect khi RabbitMQ connection loss
    ///   - Graceful shutdown — drain in-flight messages
    ///   - Tracing/metrics integration
    /// </summary>
    public abstract class BackgroundWorker : BackgroundService
    {
        protected readonly ILogger Logger;
        protected readonly IMessagePublisher Publisher;
        protected readonly IIdempotencyService Idempotency;
        protected readonly IMessageConsumer Consumer;
        private readonly IServiceProvider? _serviceProvider;
        private readonly IMessageDedupService? _dedupService;

        private IDlqRepository? _dlqRepo;
        private ITransactionService? _txService;
        private bool _handledCurrentMessage;

        protected virtual int MaxRetryAttempts => 3;
        protected virtual int BaseRetryDelayMs => 2_000;
        protected virtual bool EnableDlq => true;

        protected BackgroundWorker(
            ILogger logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            IMessageDedupService? dedupService = null,
            IServiceProvider? serviceProvider = null)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            Idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
            Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            _dedupService = dedupService;
            _serviceProvider = serviceProvider;
            if (_serviceProvider != null)
                EnsureServices(_serviceProvider);
        }

        public abstract TransactionStep Step { get; }
        protected abstract Task ProcessCoreAsync(QueueMessage message, CancellationToken ct);
        protected virtual Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;
        protected virtual Task OnStopAsync(CancellationToken ct) => Task.CompletedTask;

        private void EnsureServices(IServiceProvider? sp)
        {
            if (sp == null) return;
            _dlqRepo ??= (IDlqRepository?)sp.GetService(typeof(IDlqRepository));
            _txService ??= (ITransactionService?)sp.GetService(typeof(ITransactionService));
        }

        /// <summary>
        /// Optional dedup check before processing. Override to change behaviour per-worker.
        /// Returns true if the message should be skipped (already processed or step already passed).
        /// </summary>
        protected virtual async Task<bool> TrySkipDuplicateAsync(QueueMessage message)
        {
            // Pattern 1: Redis message-level dedup
            if (_dedupService != null)
            {
                var dedupKey = $"dedup:worker:{Step}:{message.MessageId}";
                if (!await _dedupService.TryAcquireAsync(dedupKey))
                {
                    Logger.LogTrace("Duplicate message {MessageId} at step {Step} skipped",
                        message.MessageId, Step);
                    return true;
                }
            }

            // Pattern 2: Step Gate — check if transaction already passed this step
            if (_txService != null)
            {
                try
                {
                    var tx = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich);
                    if (tx != null && (tx.CurrentStep > Step ||
                        tx.CurrentStatus == TransactionStatus.Completed ||
                        tx.CurrentStatus == TransactionStatus.Cancelled))
                    {
                        Logger.LogInformation(
                            "Transaction {TxId} already passed step {Step} (status={Status}). Skipping.",
                            message.MaGiaoDich, Step, tx.CurrentStatus);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "Step Gate check failed for {TxId} at step {Step}. Proceeding anyway.",
                        message.MaGiaoDich, Step);
                }
            }

            return false;
        }

        public override async Task StopAsync(CancellationToken ct)
        {
            Logger.LogWarning(
                "{Worker} graceful shutdown initiated — draining in-flight messages...",
                GetType().Name);

            // Signal internal cancellation
            await base.StopAsync(ct);

            // Wait for in-flight to drain (max 30s)
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    var inFlight = MetricsCollector.CurrentInFlight;
                    if (inFlight <= 0) break;
                    Logger.LogInformation("Draining... {InFlight} in-flight", inFlight);
                    await Task.Delay(1000, ct);
                }
                catch { break; }
            }

            Logger.LogInformation("{Worker} shutdown complete.", GetType().Name);
        }

        protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation(
                "{Worker} (Step={Step}) started, listening on queue {Queue}",
                GetType().Name, Step, Consumer.QueueName);

            await OnStartAsync(stoppingToken);

            // Reconnect loop: nếu connection loss, tự động retry
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Consumer.StartConsumingAsync(MessageHandler, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "{Worker} connection lost. Reconnecting in 5s...",
                        GetType().Name);
                    try { await Task.Delay(5_000, stoppingToken); } catch { break; }
                }
            }

            await OnStopAsync(stoppingToken);
            Logger.LogInformation("{Worker} stopped.", GetType().Name);
        }

        private async Task MessageHandler(QueueMessage message, CancellationToken ct)
        {
            using var _ = Logger.BeginScope(new { MaGiaoDich = message.MaGiaoDich, Step = Step });
            _handledCurrentMessage = false;

            // Dedup check: skip if already processed or transaction already past this step
            if (await TrySkipDuplicateAsync(message))
            {
                _handledCurrentMessage = true;
                return;
            }

            try
            {
                await ProcessCoreAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _handledCurrentMessage = false;
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Error processing message for TxId={TxId} at step {Step} (attempt {Attempt})",
                    message.MaGiaoDich, Step, message.Attempt);
                await HandleFailureAsync(message, ex, ct);
            }

            if (!_handledCurrentMessage)
            {
                throw new Exception("Processing failed — will be retried via requeue");
            }
        }

        private async Task HandleFailureAsync(QueueMessage message, Exception exception, CancellationToken ct)
        {
            int attempt = message.Attempt + 1;

            try
            {
                if (attempt < MaxRetryAttempts)
                {
                    var delayMs = Math.Min(BaseRetryDelayMs * (int)Math.Pow(2, attempt), 60_000);

                    await Publisher.PublishAsync(new QueueMessage
                    {
                        MessageId = Guid.NewGuid().ToString("N"),
                        MaGiaoDich = message.MaGiaoDich,
                        TenantId = message.TenantId,
                        Provider = message.Provider,
                        Step = message.Step,
                        Attempt = attempt,
                        TraceId = message.TraceId,
                        Payload = message.Payload,
                        NextRunAt = DateTime.UtcNow.AddMilliseconds(delayMs),
                    });

                    Logger.LogWarning(
                        "Scheduled retry #{Attempt} for {MaGiaoDich} at step {Step} in {Delay}ms",
                        attempt, message.MaGiaoDich, Step, delayMs);

                    if (_txService != null)
                    {
                        try { await _txService.RecordEventAsync(message.MaGiaoDich,
                            TransactionEventType.ErrorOccurred,
                            $"Retry #{attempt} at step {Step}: {exception.Message}"); } catch { }
                    }
                }
                else if (EnableDlq && _dlqRepo != null)
                {
                    Logger.LogError(
                        "Max retries ({MaxRetry}) for {MaGiaoDich} at step {Step}. Routing to DLQ.",
                        MaxRetryAttempts, message.MaGiaoDich, Step);

                    await _dlqRepo.StoreAsync(new DlqMessage
                    {
                        OriginalId = message.MessageId,
                        FailedStep = Step,
                        ErrorCode = ErrorCode.UnexpectedException,
                        ErrorMessage = $"Max retries exceeded: {exception.Message}",
                        Payload = JsonSerializer.Serialize(new
                        {
                            message.MaGiaoDich, message.TenantId, message.Provider,
                            message.Step, message.Attempt, Exception = exception.ToString()
                        }),
                        AttemptCount = attempt,
                    });

                    if (_txService != null)
                    {
                        try { await _txService.UpdateStatusAsync(
                            message.MaGiaoDich, TransactionStatus.Failed, Step); } catch { }
                    }
                }
                else
                {
                    Logger.LogWarning("DLQ unavailable — message will be requeued");
                }

                _handledCurrentMessage = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "HandleFailureAsync itself failed for {TxId} at step {Step}",
                    message.MaGiaoDich, Step);
                // Do NOT set _handledCurrentMessage — the caller in MessageHandler
                // will see false and throw, causing the consumer to NACK the message.
                // This prevents message loss (message will be requeued).
            }
        }
    }
}
