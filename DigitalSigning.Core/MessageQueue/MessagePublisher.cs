using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Settings;
using RabbitMQ.Client;

namespace DigitalSigning.Core.MessageQueue
{
    /// <summary>
    /// Publishes domain messages to RabbitMQ queues based on the message's Step property.
    /// Uses a thread-safe channel pool for concurrent publishing without global locks.
    /// </summary>
    public class MessagePublisher : IMessagePublisher, IDisposable
    {
        private readonly RabbitMqConnection _connection;
        private readonly string _exchangeName;
        private readonly IConnection _conn;
        private readonly ConcurrentBag<IModel> _channelPool = new();
        private const int MaxPoolSize = 32;

        public MessagePublisher(RabbitMqConnection connection, RabbitMqSettings settings)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _exchangeName = $"exchange.signature.{settings?.VirtualHost ?? "/"}";
            _conn = _connection.Connection;
        }

        /// <inheritdoc/>
        public Task PublishAsync(object message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var routingKey = ResolveRoutingKey(message);
            return PublishToRoutingKeyAsync(routingKey, message);
        }

        /// <inheritdoc/>
        public Task PublishToQueueAsync(string queueName, object message)
        {
            if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentNullException(nameof(queueName));
            if (message == null) throw new ArgumentNullException(nameof(message));

            // Map queue name to routing key
            var routingKey = GetRoutingKeyForQueue(queueName);
            return PublishToRoutingKeyAsync(routingKey, message);
        }

        private Task PublishToRoutingKeyAsync(string routingKey, object message)
        {
            if (string.IsNullOrWhiteSpace(routingKey)) throw new ArgumentNullException(nameof(routingKey));
            if (message == null) throw new ArgumentNullException(nameof(message));

            var body = SerializeMessage(message);
            var channel = RentChannel();

            try
            {
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "application/json";
                properties.MessageId = Guid.NewGuid().ToString("N");
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // Use routing key (step-level) for Direct exchange
                channel.BasicPublish(
                    exchange: _exchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body);
            }
            finally
            {
                ReturnChannel(channel);
            }

            return Task.CompletedTask;
        }

        private IModel RentChannel()
        {
            if (_channelPool.TryTake(out var channel))
            {
                if (channel.IsOpen)
                    return channel;
                channel.Dispose();
            }
            return _conn.CreateModel();
        }

        private void ReturnChannel(IModel channel)
        {
            if (_channelPool.Count < MaxPoolSize && channel.IsOpen)
                _channelPool.Add(channel);
            else
                channel.Dispose();
        }

        private static byte[] SerializeMessage(object message)
        {
            var json = JsonSerializer.Serialize(message);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Resolves the queue name from the message's Step property.
        /// Works for both <see cref="BaseMessage"/> and <see cref="QueueMessage"/>.
        /// </summary>
        private static string ResolveRoutingKey(object message)
        {
            TransactionStep step;

            if (message is BaseMessage baseMsg)
                step = baseMsg.Step;
            else if (message is QueueMessage qMsg)
                step = qMsg.Step;
            else
                throw new InvalidOperationException(
                    $"Message type '{message.GetType().Name}' does not contain a known Step property.");

            return GetRoutingKeyForStep(step);
        }

        private static string GetRoutingKeyForStep(TransactionStep step)
        {
            return step switch
            {
                TransactionStep.FilePrepare       => RabbitMqTopologyInitializer.FilePrepareRoutingKey,
                TransactionStep.Hash              => RabbitMqTopologyInitializer.HashRoutingKey,
                TransactionStep.ProviderRequest   => RabbitMqTopologyInitializer.ProviderRoutingKey,
                TransactionStep.WaitingUserConfirm => RabbitMqTopologyInitializer.WaitingRoutingKey,
                TransactionStep.ProviderSigning   => RabbitMqTopologyInitializer.ProviderRoutingKey,
                TransactionStep.AppendSignature   => RabbitMqTopologyInitializer.AppendRoutingKey,
                TransactionStep.Upload            => RabbitMqTopologyInitializer.UploadRoutingKey,
                TransactionStep.WebhookNotify     => RabbitMqTopologyInitializer.WebhookRoutingKey,
                _ => throw new InvalidOperationException($"No routing key mapped for step '{step}'.")
            };
        }

        private static string GetRoutingKeyForQueue(string queueName)
        {
            return queueName switch
            {
                RabbitMqTopologyInitializer.FilePrepareQueue => RabbitMqTopologyInitializer.FilePrepareRoutingKey,
                RabbitMqTopologyInitializer.HashQueue => RabbitMqTopologyInitializer.HashRoutingKey,
                RabbitMqTopologyInitializer.ProviderQueue => RabbitMqTopologyInitializer.ProviderRoutingKey,
                RabbitMqTopologyInitializer.WaitingQueue => RabbitMqTopologyInitializer.WaitingRoutingKey,
                RabbitMqTopologyInitializer.AppendQueue => RabbitMqTopologyInitializer.AppendRoutingKey,
                RabbitMqTopologyInitializer.UploadQueue => RabbitMqTopologyInitializer.UploadRoutingKey,
                RabbitMqTopologyInitializer.WebhookQueue => RabbitMqTopologyInitializer.WebhookRoutingKey,
                _ => queueName // fallback: use queue name directly
            };
        }

        public void Dispose()
        {
            while (_channelPool.TryTake(out var channel))
            {
                try { channel.Close(); } catch { }
                channel.Dispose();
            }
        }
    }
}
