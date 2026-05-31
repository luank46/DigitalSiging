using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Phát hiện và khôi phục các transaction bị treo (hanging transactions).
    ///
    /// Transaction được coi là "treo" nếu:
    ///   - Ở trạng thái PreparingFile/Hashing/ProviderAuthorizing quá 10 phút
    ///   - Ở trạng thái WaitingUserConfirm nhưng WaitingExpireAt đã qua
    ///   - Ở trạng thái ProviderSigning/AppendingSignature quá 10 phút
    ///
    /// Khi phát hiện transaction treo, service sẽ publish lại message vào queue
    /// để worker xử lý tiếp.
    ///
    /// Chạy mỗi 5 phút.
    /// </summary>
    public class TransactionRecoveryService : BackgroundService
    {
        private readonly ITransactionRepository _txRepo;
        private readonly IMessagePublisher _publisher;
        private readonly ILogger<TransactionRecoveryService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan HangingThreshold = TimeSpan.FromMinutes(10);

        // Các trạng thái "đang xử lý" — nếu ở lâu hơn HangingThreshold là treo
        private static readonly HashSet<TransactionStatus> ProcessingStatuses = new()
        {
            TransactionStatus.PreparingFile,
            TransactionStatus.Hashing,
            TransactionStatus.ProviderAuthorizing,
            TransactionStatus.ProviderSigning,
            TransactionStatus.AppendingSignature,
            TransactionStatus.UploadingSignedFile,
        };

        public TransactionRecoveryService(
            ITransactionRepository txRepo,
            IMessagePublisher publisher,
            ILogger<TransactionRecoveryService> logger)
        {
            _txRepo = txRepo ?? throw new ArgumentNullException(nameof(txRepo));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "TransactionRecoveryService started — checking every {Interval}m",
                Interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RecoverHangingTransactionsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Transaction recovery cycle failed");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RecoverHangingTransactionsAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var threshold = now.Add(-HangingThreshold);
            var recovered = 0;

            // 1. Transaction ở trạng thái processing quá lâu
            foreach (var status in ProcessingStatuses)
            {
                var hanging = await _txRepo.GetByStatusAndAgeAsync(status, threshold);
                foreach (var tx in hanging)
                {
                    await PublishRecoveryAsync(tx, $"Hanging at {status} since {tx.UpdatedAt}");
                    recovered++;
                }
            }

            // 2. Transaction chờ user confirm nhưng đã timeout
            var expiredWaiting = await _txRepo.GetExpiredWaitingAsync(now);
            foreach (var tx in expiredWaiting)
            {
                // Mark as timeout
                await _txRepo.UpdateStatusAsync(tx.MaGiaoDich,
                    TransactionStatus.Timeout, TransactionStep.WaitingUserConfirm);
                _logger.LogWarning("Recovery: tx {Tx} waiting timeout — marked as Timeout", tx.MaGiaoDich);
                recovered++;
            }

            if (recovered > 0)
                _logger.LogInformation("TransactionRecovery: recovered {Count} hanging transactions", recovered);
        }

        private async Task PublishRecoveryAsync(Transaction tx, string reason)
        {
            _logger.LogWarning(
                "Recovery: tx {Tx} — {Reason}. Re-publishing to queue step={Step}",
                tx.MaGiaoDich, reason, tx.CurrentStep);

            await _publisher.PublishAsync(new QueueMessage
            {
                MaGiaoDich = tx.MaGiaoDich,
                Provider = tx.ProviderType,
                Step = tx.CurrentStep,
                TenantId = tx.TenantId,
                Attempt = tx.RetryCount + 1,
                TraceId = tx.MaGiaoDich,
            });

            // Increment retry count
            await _txRepo.IncrementRetryCountAsync(tx.MaGiaoDich);
        }
    }
}
