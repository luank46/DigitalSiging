using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Observability.Tracing;
using DigitalSigning.Core.Providers;
using DigitalSigning.Core.Providers.Vnpt;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.WorkerBase;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DigitalSigning.Workers.Provider
{
    public class Worker : BackgroundWorker
    {
        private readonly ITransactionService _txService;
        private readonly IProviderFactory _providerFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionMultiplexer _redis;

        public Worker(
            ILogger<Worker> logger,
            IMessagePublisher publisher,
            IIdempotencyService idempotency,
            IMessageConsumer consumer,
            ITransactionService txService,
            IProviderFactory providerFactory,
            IServiceProvider serviceProvider,
            IConnectionMultiplexer redis)
            : base(logger, publisher, idempotency, consumer, serviceProvider)
        {
            _txService = txService;
            _providerFactory = providerFactory;
            _serviceProvider = serviceProvider;
            _redis = redis;
        }

        public override TransactionStep Step => TransactionStep.ProviderRequest;

        protected override async Task ProcessCoreAsync(QueueMessage message, CancellationToken ct)
        {
            using var activity = ActivitySourceExtensions.StartActivity(
                ActivitySourceExtensions.ProviderRequest, message.MaGiaoDich, message.TenantId);

            Logger.LogInformation("ProviderWorker: processing {MaGiaoDich}, Provider={Provider}",
                message.MaGiaoDich, message.Provider);

            var transaction = await _txService.GetByMaGiaoDichAsync(message.MaGiaoDich)
                ?? throw new InvalidOperationException($"Transaction {message.MaGiaoDich} not found");

            // Determine if this is a waiting poll or first call
            bool isWaiting = message.Step == TransactionStep.WaitingUserConfirm
                || (transaction.CurrentStep == TransactionStep.WaitingUserConfirm);

            // Build sign request
            var signRequest = new ProviderSignRequest
            {
                MaGiaoDich = message.MaGiaoDich,
                TenantId = message.TenantId,
                Provider = message.Provider,
                MaNhaPhatHanh = transaction.MaNhaPhatHanh,
                Username = transaction.UserName,
                Password = transaction.Password,
                SerialNumber = transaction.SeriNumber,
                Certificate = transaction.Certificate,
                DataHashes = transaction.DataHashes,
                Hash = transaction.Hash,
                Signatures = transaction.Signatures,
                FileType = transaction.LoaiKy ?? "XML",
                Notification = transaction.DescriptionNotifi,
                IsWaiting = isWaiting,
                ProviderSessionId = transaction.ProviderSessionId,
                Sad = transaction.Sad,
                Attempt = message.Attempt,
                // Build file list from unsigned files
                Files = transaction.UnsignedFiles?.ConvertAll(f => new ProviderFileInfo
                {
                    FileByteId = f.FileByteId,
                    FileUrl = f.FileUrl,
                    FileName = f.FileName,
                    Md5Hash = f.Md5Hash,
                    HashData = f.HashData,
                    NodeSignerPath = f.NodeSignerPath,
                    NodeSignerText = f.NodeSignerText,
                    NodeSignerId = f.NodeSignerId,
                    NodeDataSignId = f.NodeDataSignId,
                    NodeDataSignIds = f.NodeDataSignIds,
                })
            };

            // Resolve provider and sign
            var provider = _providerFactory.GetProvider(message.Provider);
            var result = await provider.SignAsync(signRequest, ct);

            if (result.ErrorCode.HasValue)
            {
                // Throw exception to trigger BackgroundWorker's retry/DLQ handling
                throw new Exception($"Provider error: {result.ErrorMessage}");
            }

            if (result.IsWaiting)
            {
                Logger.LogInformation("ProviderWorker: waiting for user confirmation for {MaGiaoDich}",
                    message.MaGiaoDich);

                // Save session info
                await _txService.UpdateProviderSessionAsync(
                    message.MaGiaoDich,
                    result.ProviderSessionId ?? "",
                    result.NextCheckAt ?? DateTime.UtcNow.AddMinutes(5));

                // Store in Redis sorted set for the Waiting worker
                var db = _redis.GetDatabase();
                var score = new DateTimeOffset(
                    result.NextCheckAt ?? DateTime.UtcNow.AddSeconds(30)).ToUnixTimeSeconds();
                await db.SortedSetAddAsync("waiting:signatures",
                    message.MaGiaoDich, score);

                PrometheusMetrics.RecordStepDuration(TransactionStep.WaitingUserConfirm, 0,
                    message.Provider.ToString());

                // Publish to waiting queue
                var waitingMsg = new QueueMessage
                {
                    MaGiaoDich = message.MaGiaoDich,
                    TenantId = message.TenantId,
                    Provider = message.Provider,
                    Step = TransactionStep.WaitingUserConfirm,
                    TraceId = message.TraceId,
                    Attempt = message.Attempt,
                };
                await Publisher.PublishAsync(waitingMsg);
            }
            else
            {
                Logger.LogInformation("ProviderWorker: signing complete for {MaGiaoDich}",
                    message.MaGiaoDich);

                // Save signatures and signed files
                if (result.Signatures != null)
                {
                    await _txService.UpdateSignatureDataAsync(
                        message.MaGiaoDich,
                        result.Signatures,
                        result.DataHashes ?? new(),
                        result.Hash ?? new());
                }

                if (result.SignedFiles != null && result.SignedFiles.Count > 0)
                {
                    await _txService.SetSignedFilesAsync(message.MaGiaoDich, result.SignedFiles);
                }

                // Update final status
                if (!string.IsNullOrEmpty(result.MaTrangThaiKy))
                {
                    await _txService.UpdateFinalStatusAsync(
                        message.MaGiaoDich, result.MaTrangThaiKy, result.ErrorMessage, result.Sad);
                }

                await _txService.UpdateStatusAsync(
                    message.MaGiaoDich, TransactionStatus.ProviderSigning, TransactionStep.AppendSignature);

                // Note: RecordTransactionCompleted và CurrentInFlight-- được xử lý ở WebhookWorker
                // khi transaction thực sự hoàn thành

                // Publish append signature step
                var nextMsg = new QueueMessage
                {
                    MaGiaoDich = message.MaGiaoDich,
                    TenantId = message.TenantId,
                    Provider = message.Provider,
                    Step = TransactionStep.AppendSignature,
                    TraceId = message.TraceId,
                    Attempt = message.Attempt,
                };
                await Publisher.PublishAsync(nextMsg);
            }
        }
    }
}
