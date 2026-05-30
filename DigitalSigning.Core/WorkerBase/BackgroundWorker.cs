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
    /// Retry strategy:
    ///   - Retry → publish message mới (attempt+1) đến exchange → ACK message cũ
    ///   - Hết retries → DLQ → NACK (không requeue)
    ///   - Không retry + không DLQ → NACK + requeue (safe fallback)
    ///
    /// Lỗi handler không throw ra ngoài — đã được xử lý trong retry/DLQ logic.
    /// </summary>
    public abstract class BackgroundWorker : BackgroundService
    {
        protected readonly ILogger Logger;
        protected readonly IMessagePublisher Publisher;
        protected readonly IIdempotencyService Idempotency;
        protected readonly IMessageConsumer Consumer;
        private readonly IServiceProvider? _serviceProvider;

        private IDlqRepository? _dlqRepo;
        private ITransactionService? _txService;

        private bool _handledCurrentMessage; // flag: retry đã được publish

        protected virtual int MaxRetryAttempts => 3;
        protected virtual int BaseRetryDelayMs => 2_000;
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

        protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation(
                "{Worker} (Step={Step}) started, listening on queue {Queue}",
                GetType().Name, Step, Consumer.QueueName);

            try
            {
                await OnStartAsync(stoppingToken);

                await Consumer.StartConsumingAsync(
                    async (message, ct) =>
                    {
                        using var _ = Logger.BeginScope(new { MaGiaoDich = message.MaGiaoDich, Step = Step });
                        _handledCurrentMessage = false;

                        try
                        {
                            await ProcessCoreAsync(message, ct);

                            // Nếu ProcessCoreAsync không throw → thành công
                            // Nếu có retry được publish từ HandleFailureAsync, _handledCurrentMessage = true
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // Graceful shutdown — rethrow để nack+requeue
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

                        // Nếu message đã được xử lý (retry published hoặc DLQ'd) → throw exception
                        // để consumer nack (không requeue vì đã có message mới / DLQ)
                        // Nếu không xử lý được → throw exception với requeue=true
                        // (consumer nack với requeue=true để retry queue tự nhiên)

                        // TH1: đã publish retry message → OK, message cũ được ack
                        // TH2: đã DLQ → OK, message cũ được ack
                        // TH3: lỗi ko xử lý được → NACK+requeue=true (exception throw)
                        if (!_handledCurrentMessage)
                        {
                            throw new Exception("Processing failed — will be retried via requeue");
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
        /// Handle failure: retry with backoff hoặc route to DLQ.
        /// Set _handledCurrentMessage = true nếu đã publish retry hoặc DLQ.
        /// </summary>
        private async Task HandleFailureAsync(QueueMessage message, Exception exception, CancellationToken ct)
        {
            int attempt = message.Attempt + 1;

            if (attempt < MaxRetryAttempts)
            {
                // Retry with exponential backoff — publish message mới
                int delayMs = Math.Min(BaseRetryDelayMs * (int)Math.Pow(2, attempt), 60_000);

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
                    NextRunAt = DateTime.UtcNow.AddMilliseconds(delayMs),
                };

                await Publisher.PublishAsync(retryMessage);

                Logger.LogWarning(
                    "Scheduled retry #{Attempt} for {MaGiaoDich} at step {Step} in {Delay}ms",
                    attempt, message.MaGiaoDich, Step, delayMs);

                _handledCurrentMessage = true;

                if (_txService != null)
                {
                    try
                    {
                        await _txService.RecordEventAsync(
                            message.MaGiaoDich,
                            TransactionEventType.ErrorOccurred,
                            $"Retry #{attempt} at step {Step}: {exception.Message}");
                    }
                    catch { }
                }
            }
            else if (EnableDlq && _dlqRepo != null)
            {
                // Max retries exceeded — route to DLQ
                Logger.LogError(
                    "Max retries ({MaxRetry}) exceeded for {MaGiaoDich} at step {Step}. Routing to DLQ.",
                    MaxRetryAttempts, message.MaGiaoDich, Step);

                try
                {
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

                    Logger.LogInformation("DLQ entry created for {MaGiaoDich}", message.MaGiaoDich);
                    _handledCurrentMessage = true;

                    if (_txService != null)
                    {
                        try
                        {
                            await _txService.UpdateStatusAsync(message.MaGiaoDich, TransactionStatus.Failed, Step);
                            await _txService.RecordEventAsync(message.MaGiaoDich,
                                TransactionEventType.ErrorOccurred,
                                $"DLQ after {attempt} attempts: {exception.Message}");
                        }
                        catch { }
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
                    "DLQ disabled for {MaGiaoDich} at step {Step} — message will be requeued",
                    message.MaGiaoDich, Step);
                // _handledCurrentMessage remains false → consumer sẽ nack+requeue
            }
        }
    }
}
