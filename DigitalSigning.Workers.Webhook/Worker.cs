using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

namespace DigitalSigning.Workers.Webhook
{
    public class Worker : BackgroundWorker
    {
        private readonly ITransactionService _txService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessagePublisher _publisher;
        private readonly FileLockService _fileLock;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            ITransactionService txService,
            IHttpClientFactory httpClientFactory,
            FileLockService fileLock)
            : base(logger, publisher, idempotency, consumer)
        {
            _txService = txService;
            _httpClientFactory = httpClientFactory;
            _publisher = publisher;
            _fileLock = fileLock;
        }

        public override TransactionStep Step => TransactionStep.WebhookNotify;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.WebhookNotify, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("WebhookWorker: processing {MaGiaoDich}", message.MaGiaoDich);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            // Send webhook if URL is configured
            if (!string.IsNullOrEmpty(transaction.UrlWebHook))
            {
                try
                {
                    var payload = new
                    {
                        maGiaoDich = transaction.MaGiaoDich,
                        maTrangThaiKy = transaction.MaTrangThaiKy ?? transaction.CurrentStatus.ToString(),
                        message = transaction.Message ?? "",
                        signedFiles = transaction.SignedFiles?.ConvertAll(f => new
                        {
                            md5Hash = f.Md5Hash,
                            fileName = f.FileName,
                            signature = f.Signature,
                            xmlSignature = f.XmlSignature
                        })
                    };

                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(transaction.UrlWebHook, content, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.LogInformation("WebhookWorker: webhook sent successfully to {Url} for {MaGiaoDich}",
                            transaction.UrlWebHook, message.MaGiaoDich);
                    }
                    else
                    {
                        Logger.LogWarning("WebhookWorker: webhook returned {StatusCode} for {MaGiaoDich}",
                            response.StatusCode, message.MaGiaoDich);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "WebhookWorker: failed to send webhook for {MaGiaoDich}",
                        message.MaGiaoDich);
                    // Don't fail the transaction — webhook failure is non-fatal
                }
            }

            // Mark transaction as Completed
            await _txService.UpdateStatusAsync(
                message.MaGiaoDich, TransactionStatus.Completed, TransactionStep.Completed);

            await _txService.UpdateFinalStatusAsync(
                message.MaGiaoDich,
                transaction.MaTrangThaiKy ?? "DA_KY",
                transaction.Message ?? "Completed",
                transaction.Sad);

            // Record event
            await _txService.RecordEventAsync(
                message.MaGiaoDich,
                TransactionEventType.TransactionCompleted,
                $"Transaction completed");

            PrometheusMetrics.RecordTransactionCompleted(
                transaction.ProviderType.ToString(), transaction.TenantId, 0);
            MetricsCollector.DecrementInFlight();

            // Release file locks để người khác có thể ký
            await ReleaseFileLocksAsync(transaction);

            Logger.LogInformation("WebhookWorker: completed for {MaGiaoDich}", message.MaGiaoDich);
        }

        /// <summary>
        /// Release file-level Redis locks for all unsigned files in this transaction.
        /// Cho phép người khác ký cùng file (học bạ) sau khi giao dịch hoàn tất.
        /// </summary>
        private async Task ReleaseFileLocksAsync(Transaction transaction)
        {
            if (transaction.UnsignedFiles == null) return;

            foreach (var file in transaction.UnsignedFiles)
            {
                if (!string.IsNullOrEmpty(file.Md5Hash))
                {
                    try
                    {
                        await _fileLock.ReleaseByMd5Async(file.Md5Hash);
                        Logger.LogInformation("WebhookWorker: released file lock for md5={Md5}", file.Md5Hash);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to release file lock for md5={Md5}", file.Md5Hash);
                    }
                }
            }
        }
    }
}
