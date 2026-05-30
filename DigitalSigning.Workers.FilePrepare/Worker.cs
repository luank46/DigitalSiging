using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace DigitalSigning.Workers.FilePrepare
{
    public class Worker : BackgroundWorker
    {
        private readonly IFileDownloader _downloader;
        private readonly ITransactionService _txService;
        private readonly IGridFsService _gridFs;
        private readonly IMessagePublisher _publisher;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            IFileDownloader downloader,
            ITransactionService txService,
            IGridFsService gridFs)
            : base(logger, publisher, idempotency, consumer)
        {
            _downloader = downloader;
            _txService = txService;
            _gridFs = gridFs;
            _publisher = publisher;
        }

        public override TransactionStep Step => TransactionStep.FilePrepare;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.FilePrepare, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("FilePrepareWorker: processing {MaGiaoDich}",
                message.MaGiaoDich);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            // Get file URLs from transaction's unsigned files, or from message payload
            var fileUrls = new List<string>();

            if (transaction.UnsignedFiles != null)
            {
                foreach (var uf in transaction.UnsignedFiles)
                {
                    if (!string.IsNullOrEmpty(uf.FileUrl))
                        fileUrls.Add(uf.FileUrl);
                }
            }

            // Also check message payload for URLs (legacy compatibility)
            if (fileUrls.Count == 0 && !string.IsNullOrEmpty(message.Payload))
            {
                try
                {
                    var payloadUrls = System.Text.Json.JsonSerializer.Deserialize<string[]>(message.Payload);
                    if (payloadUrls != null) fileUrls.AddRange(payloadUrls);
                }
                catch { /* not JSON array of URLs */ }
            }

            if (fileUrls.Count == 0)
            {
                Logger.LogWarning("FilePrepareWorker: no files to prepare for {MaGiaoDich}",
                    message.MaGiaoDich);
                // Still publish next step — maybe Hash can proceed without files
                await PublishNextAsync(message, TransactionStep.Hash, ct);
                return;
            }

            // Download each file, upload to GridFS, create UnsignedFile records
            var unsignedFiles = new List<UnsignedFile>();

            foreach (var url in fileUrls)
            {
                Logger.LogInformation("FilePrepareWorker: downloading {Url} for {MaGiaoDich}",
                    url, message.MaGiaoDich);

                var localPath = await _downloader.DownloadAsync(url, ct);

                var fileBytes = await File.ReadAllBytesAsync(localPath, ct);

                // Compute MD5 hash
                var md5Hash = ComputeMd5(fileBytes);

                // Upload to GridFS
                var fileName = Path.GetFileName(url) ?? $"file_{Guid.NewGuid():N}";
                var fileByteId = await _gridFs.UploadFileAsync(fileBytes, fileName, ct);

                // Clean up temp file
                try { File.Delete(localPath); } catch { }

                var unsignedFile = new UnsignedFile
                {
                    Md5Hash = md5Hash,
                    FileName = fileName,
                    FileUrl = url,
                    FileByteId = fileByteId
                };

                unsignedFiles.Add(unsignedFile);
            }

            // Store file references in transaction
            foreach (var uf in unsignedFiles)
            {
                await _txService.AppendUnsignedFileAsync(message.MaGiaoDich, uf);
            }

            // Update status
            await _txService.UpdateStatusAsync(
                message.MaGiaoDich, TransactionStatus.PreparingFile, TransactionStep.FilePrepare);

            // Record metrics
            PrometheusMetrics.RecordStepDuration(TransactionStep.FilePrepare, 0,
                transaction.ProviderType.ToString());
            MetricsCollector.CurrentInFlight++;

            // Publish next step
            await PublishNextAsync(message, TransactionStep.Hash, ct);

            Logger.LogInformation("FilePrepareWorker: completed for {MaGiaoDich} ({FileCount} files)",
                message.MaGiaoDich, unsignedFiles.Count);
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

        private static string ComputeMd5(byte[] bytes)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
