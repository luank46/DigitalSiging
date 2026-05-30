using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Observability.Tracing;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.WorkerBase;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Workers.Upload
{
    public class Worker : BackgroundWorker
    {
        private readonly ITransactionService _txService;
        private readonly IGridFsService _gridFs;
        private readonly IMessagePublisher _publisher;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            ITransactionService txService,
            IGridFsService gridFs)
            : base(logger, publisher, idempotency, consumer)
        {
            _txService = txService;
            _gridFs = gridFs;
            _publisher = publisher;
        }

        public override TransactionStep Step => TransactionStep.Upload;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.Upload, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("UploadWorker: processing {MaGiaoDich}", message.MaGiaoDich);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            if (transaction.SignedFiles == null || transaction.SignedFiles.Count == 0)
            {
                Logger.LogWarning("UploadWorker: no signed files for {MaGiaoDich}", message.MaGiaoDich);
                await PublishNextAsync(message, TransactionStep.WebhookNotify, ct);
                return;
            }

            // For each signed file that has GridFS bytes, mark it
            // In production, this would upload to an external file service
            foreach (var file in transaction.SignedFiles)
            {
                Logger.LogInformation("UploadWorker: file {FileName} ready (Md5={Md5})",
                    file.FileName, file.Md5Hash);
            }

            // Update status
            await _txService.UpdateStatusAsync(
                message.MaGiaoDich, TransactionStatus.UploadingSignedFile, TransactionStep.Upload);

            PrometheusMetrics.RecordStepDuration(TransactionStep.Upload, 0,
                transaction.ProviderType.ToString());

            // Publish next step
            await PublishNextAsync(message, TransactionStep.WebhookNotify, ct);

            Logger.LogInformation("UploadWorker: completed for {MaGiaoDich}", message.MaGiaoDich);
        }

        private async Task PublishNextAsync(QueueMessage current, TransactionStep nextStep, CancellationToken ct)
        {
            var nextMsg = new QueueMessage
            {
                MaGiaoDich = current.MaGiaoDich,
                TenantId = current.TenantId,
                Provider = current.Provider,
                Step = nextStep,
                TraceId = current.TraceId,
                Attempt = current.Attempt,
            };
            await _publisher.PublishAsync(nextMsg);
        }
    }
}
