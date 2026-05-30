using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DigitalSigning.Core.MessageQueue.Consumers
{
    /// <summary>
    /// Generic RabbitMQ message consumer.
    /// Deserializes JSON payloads into <see cref="QueueMessage"/> and dispatches each
    /// to a handler provided by the caller (typically <see cref="WorkerBase.BackgroundWorker"/>).
    /// Handles ack/nack, logging, and prefetch configuration.
    /// </summary>
    public sealed class MessageConsumer : Interfaces.IMessageConsumer, IDisposable
    {
        private readonly ILogger<MessageConsumer> _logger;
        private readonly string _queueName;
        private readonly IModel _channel;

        public MessageConsumer(ILogger<MessageConsumer> logger, RabbitMqConnection connection, string queueName)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));

            if (connection?.Connection == null)
                throw new ArgumentException("RabbitMqConnection must be initialized.", nameof(connection));

            _channel = connection.Connection.CreateModel();
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
        }

        /// <inheritdoc/>
        public string QueueName => _queueName;

        /// <inheritdoc/>
        public Task StartConsumingAsync(Func<QueueMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var consumer = new EventingBasicConsumer(_channel);

            consumer.Received += async (_, ea) =>
            {
                QueueMessage? message = null;
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray());

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    message = JsonSerializer.Deserialize<QueueMessage>(json, options);

                    if (message == null)
                    {
                        _logger.LogWarning("Failed to deserialize message from queue {Queue}. DeliveryTag={Tag}",
                            _queueName, ea.DeliveryTag);
                        _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    _logger.LogDebug("Processing message TxId={TxId} from queue {Queue}",
                        message.MaGiaoDich, _queueName);

                    await handler(message, cancellationToken);

                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (OperationCanceledException)
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process message TxId={TxId} from queue {Queue}",
                        message?.MaGiaoDich ?? "(unknown)", _queueName);

                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("Consumer started on queue {Queue}. Waiting for messages...", _queueName);

            // Block until cancellation.
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public void Dispose()
        {
            try { _channel?.Close(); } catch { }
            _channel?.Dispose();
        }
    }
}