using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Interfaces;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Observability.Tracing;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.WorkerBase;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Workers.Hash
{
    public class Worker : BackgroundWorker
    {
        private readonly IHashCalculator _hasher;
        private readonly ITransactionService _txService;
        private readonly IGridFsService _gridFs;
        private readonly IMessagePublisher _publisher;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            IHashCalculator hasher,
            ITransactionService txService,
            IGridFsService gridFs)
            : base(logger, publisher, idempotency, consumer)
        {
            _hasher = hasher;
            _txService = txService;
            _gridFs = gridFs;
            _publisher = publisher;
        }

        public override TransactionStep Step => TransactionStep.Hash;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.Hash, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("HashWorker: processing {MaGiaoDich}", message.MaGiaoDich);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            if (transaction.UnsignedFiles == null || transaction.UnsignedFiles.Count == 0)
            {
                Logger.LogWarning("HashWorker: no unsigned files for {MaGiaoDich}", message.MaGiaoDich);
                await PublishNextAsync(message, TransactionStep.ProviderRequest, ct);
                return;
            }

            // Process each unsigned file: compute hash and update GridFS
            foreach (var file in transaction.UnsignedFiles)
            {
                if (string.IsNullOrEmpty(file.FileByteId)) continue;

                var fileBytes = await _gridFs.DownloadFileAsync(file.FileByteId, ct);
                var hash = await _hasher.CalculateHashAsync(file.FileByteId, ct);

                // For legacy compatibility, also compute MD5 if not set
                if (string.IsNullOrEmpty(file.Md5Hash))
                {
                    using var md5 = System.Security.Cryptography.MD5.Create();
                    var md5Bytes = md5.ComputeHash(fileBytes);
                    file.Md5Hash = BitConverter.ToString(md5Bytes).Replace("-", "").ToLowerInvariant();
                }

                // Store hash in transaction
                file.HashData = hash;
                await _txService.AppendUnsignedFileAsync(message.MaGiaoDich, file);
            }

            // Update status
            await _txService.UpdateStatusAsync(
                message.MaGiaoDich, TransactionStatus.Hashing, TransactionStep.Hash);

            PrometheusMetrics.RecordStepDuration(TransactionStep.Hash, 0,
                transaction.ProviderType.ToString());

            // Publish next step
            await PublishNextAsync(message, TransactionStep.ProviderRequest, ct);

            Logger.LogInformation("HashWorker: completed for {MaGiaoDich} ({FileCount} files)",
                message.MaGiaoDich, transaction.UnsignedFiles.Count);
        }

        private async Task PublishNextAsync(QueueMessage current, TransactionStep nextStep, CancellationToken ct)
        {
            var nextMsg = new QueueMessage
            {
                MaGiaoDich = current.MaGiaoDich,
                TenantId = current.TenantId,
                Provider = current.Provider,
                Step = nextStep,
                Payload = current.Payload,  // forward hash data
                TraceId = current.TraceId,
                Attempt = current.Attempt,
            };
            await _publisher.PublishAsync(nextMsg);
        }
    }
}
