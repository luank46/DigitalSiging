using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Settings;
using DigitalSigning.Core.Models;
using RabbitMQ.Client;

namespace DigitalSigning.Core.MessageQueue
{
    /// <summary>
    /// Responsible for declaring exchanges, queues, and bindings used by the system.
    /// </summary>
    public interface IRabbitMqTopologyInitializer
    {
        /// <summary>
        /// Creates all required exchanges and queues based on the configuration.
        /// </summary>
        Task InitializeAsync();
    }

    /// <summary>
    /// Concrete implementation of <see cref="IRabbitMqTopologyInitializer"/>.
    ///
    /// Production topology cho 5.000 concurrent users:
    ///   - Quorum queues (HA, ổn định với tải cao)
    ///   - Direct exchange (mỗi step có routing key riêng)
    ///   - Dead-letter exchange cho message lỗi
    ///   - Retry do BackgroundWorker xử lý (publish message mới), không cần TTL queues
    /// </summary>
    public class RabbitMqTopologyInitializer : IRabbitMqTopologyInitializer
    {
        private readonly RabbitMqSettings _settings;
        private readonly IConnection _connection;
        private readonly bool _useQuorumQueues;

        // Queue names defined by step
        public const string FilePrepareQueue = "queue.file.prepare";
        public const string HashQueue = "queue.hash";
        public const string ProviderQueue = "queue.provider";
        public const string WaitingQueue = "queue.waiting";
        public const string AppendQueue = "queue.append";
        public const string UploadQueue = "queue.upload";
        public const string WebhookQueue = "queue.webhook";
        public const string DeadLetterQueue = "dlq.messages";

        // Routing keys (mỗi queue có routing key riêng để dùng Direct exchange)
        public const string FilePrepareRoutingKey = "step.file.prepare";
        public const string HashRoutingKey = "step.hash";
        public const string ProviderRoutingKey = "step.provider";
        public const string WaitingRoutingKey = "step.waiting";
        public const string AppendRoutingKey = "step.append";
        public const string UploadRoutingKey = "step.upload";
        public const string WebhookRoutingKey = "step.webhook";

        public RabbitMqTopologyInitializer(RabbitMqSettings settings, RabbitMqConnection connection)
        {
            _settings = settings;
            _connection = connection?.Connection ?? throw new ArgumentNullException(nameof(connection));
            _useQuorumQueues = Environment.GetEnvironmentVariable("RABBITMQ_QUORUM") == "true";
        }

        public Task InitializeAsync()
        {
            using var channel = _connection.CreateModel();

            // Declare main exchange – Direct exchange (mỗi step có routing key riêng)
            var exchangeName = $"exchange.signature.{_settings.VirtualHost}";
            channel.ExchangeDeclare(exchange: exchangeName,
                type: ExchangeType.Direct, durable: true);

            // Declare dead-letter exchange
            var dlxName = "exchange.dlx";
            channel.ExchangeDeclare(exchange: dlxName,
                type: ExchangeType.Fanout, durable: true);

            // Helper to declare queue với dead-letter routing + optional quorum
            void DeclareQueue(string queueName)
            {
                var args = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", dlxName },
                    { "x-dead-letter-routing-key", queueName }
                };

                if (_useQuorumQueues)
                {
                    args["x-queue-type"] = "quorum";
                    args["x-quorum-initial-group-size"] = 3;
                }

                channel.QueueDeclare(queue: queueName,
                                     durable: true,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: args);
            }

            // Declare all step‑specific queues
            DeclareQueue(FilePrepareQueue);
            DeclareQueue(HashQueue);
            DeclareQueue(ProviderQueue);
            DeclareQueue(WaitingQueue);
            DeclareQueue(AppendQueue);
            DeclareQueue(UploadQueue);
            DeclareQueue(WebhookQueue);

            // Declare dead‑letter queue (classic — chỉ 1 consumer)
            channel.QueueDeclare(queue: DeadLetterQueue,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            // Bind each step queue to the central Direct exchange
            channel.QueueBind(queue: FilePrepareQueue, exchange: exchangeName, routingKey: FilePrepareRoutingKey);
            channel.QueueBind(queue: HashQueue, exchange: exchangeName, routingKey: HashRoutingKey);
            channel.QueueBind(queue: ProviderQueue, exchange: exchangeName, routingKey: ProviderRoutingKey);
            channel.QueueBind(queue: WaitingQueue, exchange: exchangeName, routingKey: WaitingRoutingKey);
            channel.QueueBind(queue: AppendQueue, exchange: exchangeName, routingKey: AppendRoutingKey);
            channel.QueueBind(queue: UploadQueue, exchange: exchangeName, routingKey: UploadRoutingKey);
            channel.QueueBind(queue: WebhookQueue, exchange: exchangeName, routingKey: WebhookRoutingKey);

            return Task.CompletedTask;
        }
    }
}
