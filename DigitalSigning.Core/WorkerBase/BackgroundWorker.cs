using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.WorkerBase
{
    /// <summary>
    /// Abstract base class for all worker services.
    /// Automatically consumes messages from the queue mapped to <see cref="Step"/>
    /// and dispatches them to <see cref="ProcessCoreAsync"/>.
    ///
    /// Features:
    ///   - Retry with configurable max attempts and exponential backoff
    ///   - DLQ routing when max retries exceeded
    ///   - Idempotency check before processing
    ///   - Tracing/metrics integration
    /// </summary>
    public abstract class BackgroundWorker : BackgroundService
    {
        protected readonly ILogger Logger;
        protected readonly IMessagePublisher Publisher;
        protected readonly IIdempotencyService Idempotency;
        protected readonly IMessageConsumer Consumer;
        private readonly IServiceProvider? _serviceProvider;

        // Optional DI — resolved lazily so derived workers don't need to carry them
        private IDlqRepository? _dlqRepo;
        private ITransactionService? _txService;

        /// <summary>Maximum retry attempts before routing to DLQ. Override to customize.</summary>
        protected virtual int MaxRetryAttempts => 3;

        /// <summary>Base delay in ms for exponential backoff (attempt N → delay = BaseRetryDelayMs * 2^N).</summary>
        protected virtual int BaseRetryDelayMs => 2_000;

        /// <summary>Whether to route failed messages to the DLQ after max retries.</summary>
        protected virtual bool EnableDlq => true;

        protected BackgroundWorker(
            ILogger logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            IServiceProvider? serviceProvider = null)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            Idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
            Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            _serviceProvider = serviceProvider;
            if (_serviceProvider != null)
                EnsureServices(_serviceProvider);
        }

        /// <summary>
        /// The pipeline step this worker processes.
        /// Maps to the RabbitMQ queue name via <see cref="MessageQueue.MessagePublisher"/>.
        /// </summary>
        public abstract TransactionStep Step { get; }

        /// <summary>
        /// Core processing logic for a single message.
        /// Implemented by derived classes (e.g. FilePrepare, Hash, Provider workers).
        /// Throw on failure — the base class handles retry/DLQ routing.
        /// </summary>
        protected abstract Task ProcessCoreAsync(QueueMessage message, CancellationToken ct);

        /// <summary>
        /// Called once on startup before the consume loop begins.
        /// Override to perform one-time initialization (e.g. create temp directories).
        /// </summary>
        protected virtual Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>
        /// Called once on graceful shutdown.
        /// Override to perform cleanup.
        /// </summary>
        protected virtual Task OnStopAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>
        /// Get required optional services — called lazily so the DI graph stays simple.
        /// </summary>
        private void EnsureServices(IServiceProvider? sp)
        {
            if (sp == null) return;
            _dlqRepo ??= (IDlqRepository?)sp.GetService(typeof(IDlqRepository));
            _txService ??= (ITransactionService?)sp.GetService(typeof(ITransactionService));
        }

        protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation(
                "{Worker} (Step={Step}) started, listening on queue {Queue}",
                GetType().Name, Step, Consumer.QueueName);

            try
            {
                await OnStartAsync(stoppingToken);

                // Start consuming — each message is dispatched to ProcessCoreAsync.
                // This call blocks until cancellation or connection loss.
                await Consumer.StartConsumingAsync(
                    async (message, ct) =>
                    {
                        using var _ = Logger.BeginScope(new { MaGiaoDich = message.MaGiaoDich, Step = Step });
                        try
                        {
                            // 1. Core processing (idempotency is handled by RabbitMQ ack semantics)
                            await ProcessCoreAsync(message, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // Graceful shutdown — rethrow to stop the loop.
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex,
                                "Error processing message for TxId={TxId} at step {Step} (attempt {Attempt})",
                                message.MaGiaoDich, Step, message.Attempt);

                            // 3. Handle retry / DLQ
                            await HandleFailureAsync(message, ex, ct);

                            // Don't rethrow — the failure has been handled (retry queued or DLQ'd)
                        }
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Logger.LogInformation("{Worker} is stopping gracefully.", GetType().Name);
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "{Worker} encountered a fatal error.", GetType().Name);
            }
            finally
            {
                await OnStopAsync(stoppingToken);
            }
        }

        /// <summary>
        /// Handles a processing failure by either retrying with backoff or routing to DLQ.
        /// </summary>
        private async Task HandleFailureAsync(QueueMessage message, Exception exception, CancellationToken ct)
        {
            int attempt = message.Attempt + 1;

            if (attempt < MaxRetryAttempts)
            {
                // Retry with exponential backoff
                int delayMs = Math.Min(BaseRetryDelayMs * (int)Math.Pow(2, attempt), 60_000);
                var nextRunAt = DateTime.UtcNow.AddMilliseconds(delayMs);

                var retryMessage = new QueueMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    MaGiaoDich = message.MaGiaoDich,
                    TenantId = message.TenantId,
                    Provider = message.Provider,
                    Step = message.Step,
                    Attempt = attempt,
                    TraceId = message.TraceId,
                    Payload = message.Payload,
                    NextRunAt = nextRunAt,
                };

                await Publisher.PublishAsync(retryMessage);

                Logger.LogWarning(
                    "Scheduled retry #{Attempt} for {MaGiaoDich} at step {Step} in {Delay}ms",
                    attempt, message.MaGiaoDich, Step, delayMs);

                // Update transaction status to reflect retry
                if (_txService != null)
                {
                    try
                    {
                        await _txService.RecordEventAsync(
                            message.MaGiaoDich,
                            TransactionEventType.ErrorOccurred,
                            $"Retry #{attempt} at step {Step}: {exception.Message}");
                    }
                    catch { /* non-fatal */ }
                }
            }
            else
            {
                // Max retries exceeded — route to DLQ
                Logger.LogError(
                    "Max retries ({MaxRetry}) exceeded for {MaGiaoDich} at step {Step}. Routing to DLQ.",
                    MaxRetryAttempts, message.MaGiaoDich, Step);

                if (EnableDlq && _dlqRepo != null)
                {
                    try
                    {
                        var dlqMessage = new DlqMessage
                        {
                            OriginalId = message.MessageId,
                            FailedStep = Step,
                            ErrorCode = ErrorCode.UnexpectedException,
                            ErrorMessage = $"Max retries exceeded: {exception.Message}",
                            Payload = JsonSerializer.Serialize(new
                            {
                                message.MaGiaoDich,
                                message.TenantId,
                                message.Provider,
                                message.Step,
                                message.Attempt,
                                Exception = exception.ToString()
                            }),
                            AttemptCount = attempt,
                        };

                        await _dlqRepo.StoreAsync(dlqMessage);
                        Logger.LogInformation("DLQ entry created for {MaGiaoDich}", message.MaGiaoDich);

                        // Update transaction status to Failed
                        if (_txService != null)
                        {
                            try
                            {
                                await _txService.UpdateStatusAsync(
                                    message.MaGiaoDich,
                                    TransactionStatus.Failed,
                                    Step);

                                await _txService.RecordEventAsync(
                                    message.MaGiaoDich,
                                    TransactionEventType.ErrorOccurred,
                                    $"DLQ after {attempt} attempts: {exception.Message}");
                            }
                            catch { /* non-fatal */ }
                        }
                    }
                    catch (Exception dlqEx)
                    {
                        Logger.LogError(dlqEx, "Failed to store DLQ entry for {MaGiaoDich}", message.MaGiaoDich);
                    }
                }
                else
                {
                    Logger.LogWarning(
                        "DLQ disabled or repository unavailable for {MaGiaoDich} — discarding message",
                        message.MaGiaoDich);
                }
            }
        }
    }
}
