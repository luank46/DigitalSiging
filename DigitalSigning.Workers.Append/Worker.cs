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

namespace DigitalSigning.Workers.Append
{
    public class Worker : BackgroundWorker
    {
        private readonly ITransactionService _txService;
        private readonly IGridFsService _gridFs;
        private readonly IMessagePublisher _publisher;
        private readonly FileLockService _fileLock;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            IMessageDedupService dedupService,
            ITransactionService txService,
            IGridFsService gridFs,
            FileLockService fileLock)
            : base(logger, publisher, idempotency, consumer, dedupService)
        {
            _txService = txService;
            _gridFs = gridFs;
            _publisher = publisher;
            _fileLock = fileLock;
        }

        public override TransactionStep Step => TransactionStep.AppendSignature;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.AppendSignature, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("AppendWorker: processing {MaGiaoDich}", message.MaGiaoDich);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            if (transaction.Signatures == null || transaction.Signatures.Count == 0)
            {
                Logger.LogWarning("AppendWorker: no signatures for {MaGiaoDich}", message.MaGiaoDich);
                await PublishNextAsync(message, TransactionStep.Upload, ct);
                return;
            }

            // If there are signed files from Provider worker, just pass them through
            if (transaction.SignedFiles != null && transaction.SignedFiles.Count > 0)
            {
                Logger.LogInformation("AppendWorker: {Count} signed files already present for {MaGiaoDich}",
                    transaction.SignedFiles.Count, message.MaGiaoDich);
            }

            // Check file locks for each unsigned file, then create signed file records
            if (transaction.LoaiKy == "HASH" && transaction.UnsignedFiles != null)
            {
                foreach (var (file, index) in transaction.UnsignedFiles
                    .Select((f, i) => (f, i)))
                {
                    if (index >= transaction.Signatures.Count) continue;

                    // Acquire file lock before appending signature
                    if (!string.IsNullOrEmpty(file.Md5Hash))
                    {
                        var lockHandle = await _fileLock.AcquireAsync(
                            file.Md5Hash,
                            $"append:{message.MaGiaoDich}",
                            TimeSpan.FromMinutes(5), ct);

                        if (lockHandle == null)
                        {
                            Logger.LogWarning(
                                "AppendWorker: file {Md5} locked by another. Requeuing.",
                                file.Md5Hash);
                            throw new InvalidOperationException(
                                $"File {file.Md5Hash} is being signed by another transaction");
                        }
                        await using var _ = lockHandle;
                    }

                    var signedFile = new SignedFile
                    {
                        Md5Hash = file.Md5Hash ?? "",
                        FileName = file.FileName ?? $"file_{index}",
                        Signature = transaction.Signatures[index],
                        XmlSignature = file.XmlSignature,
                        NodeSignerPath = file.NodeSignerPath
                    };
                    await _txService.AppendSignedFileAsync(message.MaGiaoDich, signedFile);
                }
            }

            // Update status
            await _txService.UpdateStatusAsync(
                message.MaGiaoDich, TransactionStatus.AppendingSignature, TransactionStep.AppendSignature);

            PrometheusMetrics.RecordStepDuration(TransactionStep.AppendSignature, 0,
                transaction.ProviderType.ToString());

            // Publish next step
            await PublishNextAsync(message, TransactionStep.Upload, ct);

            Logger.LogInformation("AppendWorker: completed for {MaGiaoDich}", message.MaGiaoDich);
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
