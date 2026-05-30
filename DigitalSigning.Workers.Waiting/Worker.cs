using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Providers;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Waiting.Config;
using DigitalSigning.Core.Waiting.Interfaces;
using DigitalSigning.Core.Waiting.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Workers.Waiting
{
    /// <summary>
    /// Waiting Worker — polls Redis Sorted Set for transactions awaiting user confirmation.
    ///
    /// Flow:
    ///   1. Every PollIntervalSeconds, query Redis for expired items (score ≤ now).
    ///   2. For each expired item, acquire a distributed lock to prevent double-processing.
    ///   3. Check if the transaction has exceeded WaitingTimeoutSeconds → mark Failed.
    ///   4. Otherwise, publish a QueueMessage with Step=WaitingUserConfirm back to the Provider queue.
    ///      The ProviderWorker picks this up with IsWaiting=true, calls provider.SignAsync with polling,
    ///      and either gets the signature (user approved) or returns IsWaiting again (still pending).
    ///   5. If still waiting, re-schedule with updated nextCheckAt.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IWaitingScheduler _scheduler;
        private readonly IWaitingLockService _lockService;
        private readonly WaitingOptions _options;
        private readonly IMessagePublisher _publisher;
        private readonly ITransactionService _txService;

        public Worker(
            ILogger<Worker> logger,
            IWaitingScheduler scheduler,
            IWaitingLockService lockService,
            WaitingOptions options,
            IMessagePublisher publisher,
            ITransactionService txService)
        {
            _logger = logger;
            _scheduler = scheduler;
            _lockService = lockService;
            _options = options;
            _publisher = publisher;
            _txService = txService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "WaitingWorker started: interval={Interval}s, batch={BatchSize}, timeout={Timeout}s",
                _options.PollIntervalSeconds, _options.BatchSize, _options.WaitingTimeoutSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessWaitingItemsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WaitingWorker error in polling cycle");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
        }

        private async Task ProcessWaitingItemsAsync(CancellationToken ct)
        {
            var expiredItems = await _scheduler.GetExpiredItemsAsync(_options.BatchSize, ct);
            if (expiredItems.Length == 0)
                return;

            _logger.LogInformation("WaitingWorker: processing {Count} expired items", expiredItems.Length);

            foreach (var item in expiredItems)
            {
                if (ct.IsCancellationRequested) break;

                // Acquire distributed lock
                IAsyncDisposable? lockHandle = null;
                try
                {
                    lockHandle = await _lockService.AcquireLockAsync(
                        item.TransactionId,
                        TimeSpan.FromSeconds(_options.LockTtlSeconds),
                        ct);

                    if (lockHandle == null)
                    {
                        _logger.LogTrace("WaitingWorker: lock held by another worker for {TxId}",
                            item.TransactionId);
                        continue;
                    }

                    // Load transaction from database
                    var transaction = await _txService.GetByMaGiaoDichAsync(item.TransactionId);
                    if (transaction == null)
                    {
                        _logger.LogWarning("WaitingWorker: transaction {TxId} not found, removing from waiting",
                            item.TransactionId);
                        await _scheduler.RemoveAsync(item.TransactionId, ct);
                        continue;
                    }

                    // Check timeout
                    if (item.IsTimedOut() ||
                        transaction.CurrentStatus == TransactionStatus.Timeout ||
                        transaction.CurrentStatus == TransactionStatus.Failed)
                    {
                        _logger.LogWarning("WaitingWorker: transaction {TxId} timed out", item.TransactionId);

                        await _txService.UpdateStatusAsync(
                            item.TransactionId,
                            TransactionStatus.Timeout,
                            TransactionStep.WaitingUserConfirm);

                        await _txService.UpdateFinalStatusAsync(
                            item.TransactionId,
                            SignHelper.MA_TRANG_THAI_HET_HAN_KY,
                            "User confirmation timeout",
                            null);

                        await _txService.RecordEventAsync(
                            item.TransactionId,
                            TransactionEventType.ErrorOccurred,
                            "Waiting for user confirmation timed out");

                        await _scheduler.RemoveAsync(item.TransactionId, ct);

                        PrometheusMetrics.RecordTransactionTimeout(
                            item.Provider, item.TenantId);

                        continue;
                    }

                    // Increment attempt count
                    item.AttemptCount++;

                    // Check max attempts (prevent infinite polling)
                    int maxAttempts = _options.WaitingTimeoutSeconds / Math.Max(_options.NextCheckIntervalSeconds, 1);
                    if (item.AttemptCount > maxAttempts + 2)
                    {
                        _logger.LogWarning("WaitingWorker: max attempts reached for {TxId}", item.TransactionId);

                        await _txService.UpdateStatusAsync(
                            item.TransactionId,
                            TransactionStatus.Timeout,
                            TransactionStep.WaitingUserConfirm);

                        await _txService.UpdateFinalStatusAsync(
                            item.TransactionId,
                            SignHelper.MA_TRANG_THAI_HET_HAN_KY,
                            "Max polling attempts reached",
                            null);

                        await _scheduler.RemoveAsync(item.TransactionId, ct);

                        continue;
                    }

                    // Publish message back to Provider queue for polling
                    // Dùng Step=ProviderRequest để routing đến ProviderQueue
                    // (không dùng WaitingUserConfirm vì WaitingQueue không có consumer)
                    var providerMessage = new QueueMessage
                    {
                        MaGiaoDich = item.TransactionId,
                        TenantId = item.TenantId,
                        Provider = item.Provider switch
                        {
                            "VNPT" => ProviderType.Vnpt,
                            "VIETTEL" => ProviderType.Viettel,
                            "BKAVCA" => ProviderType.Bkav,
                            "BAN_CO_YEU" => ProviderType.Gcc,
                            "MISA-CA" => ProviderType.Misa,
                            _ => ProviderType.Vnpt
                        },
                        Step = TransactionStep.ProviderRequest,
                        TraceId = item.TransactionId,
                        Attempt = item.AttemptCount,
                    };

                    await _publisher.PublishAsync(providerMessage);

                    // Re-schedule with updated next check time
                    var nextCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _options.NextCheckIntervalSeconds;
                    await _scheduler.RescheduleAsync(item.TransactionId, nextCheck, ct);

                    _logger.LogDebug("WaitingWorker: re-published {TxId} to provider queue (attempt {Attempt})",
                        item.TransactionId, item.AttemptCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WaitingWorker: error processing {TxId}", item.TransactionId);
                }
                finally
                {
                    if (lockHandle != null)
                        await lockHandle.DisposeAsync();
                }
            }
        }
    }
}
