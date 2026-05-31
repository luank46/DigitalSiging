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
    ///
    /// Important guarantees:
    ///   - Deserialization failures → Nack + requeue=false (message không thể xử lý, discard)
    ///   - Processing failures (exception trong handler) → Nack + requeue=true
    ///     để message được retry hoặc route đến DLX/DLQ
    ///   - Channel được tạo mới mỗi lần start để tránh stale channel sau connection recovery
    /// </summary>
    public sealed class MessageConsumer : Interfaces.IMessageConsumer, IDisposable
    {
        private readonly ILogger<MessageConsumer> _logger;
        private readonly string _queueName;
        private readonly RabbitMqConnection _connection;
        private readonly ushort _prefetchCount;
        private IModel? _channel;

        public MessageConsumer(ILogger<MessageConsumer> logger, RabbitMqConnection connection, string queueName)
            : this(logger, connection, queueName, 10)
        {
        }

        public MessageConsumer(ILogger<MessageConsumer> logger, RabbitMqConnection connection, string queueName, ushort prefetchCount)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
            _prefetchCount = prefetchCount > 0 ? prefetchCount : (ushort)10;
        }

        /// <inheritdoc/>
        public string QueueName => _queueName;

        /// <inheritdoc/>
        public Task StartConsumingAsync(Func<QueueMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            // Tạo channel mới mỗi lần start — tránh stale channel
            _channel = _connection.CreateChannel();
            _channel.BasicQos(prefetchSize: 0, prefetchCount: _prefetchCount, global: false);

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
                        // Không thể deserialize → discard luôn (không retry được)
                        _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    _logger.LogDebug("Processing message TxId={TxId} from queue {Queue}",
                        message.MaGiaoDich, _queueName);

                    // Respect NextRunAt: nếu chưa đến thời gian, delay processing
                    if (message.NextRunAt.HasValue && message.NextRunAt.Value > DateTime.UtcNow)
                    {
                        var delay = message.NextRunAt.Value - DateTime.UtcNow;
                        if (delay > TimeSpan.Zero && delay < TimeSpan.FromMinutes(5))
                        {
                            _logger.LogInformation(
                                "Delaying message TxId={TxId} for {DelayMs}ms (NextRunAt={NextRunAt})",
                                message.MaGiaoDich, delay.TotalMilliseconds, message.NextRunAt.Value);
                            await Task.Delay(delay, cancellationToken);
                        }
                    }

                    await handler(message, cancellationToken);

                    // Handler thành công → Ack
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Graceful shutdown: Nack + requeue=true để message không bị mất
                    _logger.LogInformation("Message TxId={TxId} nacked (requeue=true) during shutdown",
                        message?.MaGiaoDich ?? "(unknown)");
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process message TxId={TxId} from queue {Queue}",
                        message?.MaGiaoDich ?? "(unknown)", _queueName);

                    // Handler thất bại → Nack + requeue=true
                    // Message sẽ được requeue và route đến worker khác hoặc DLX nếu có
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("Consumer started on queue {Queue}. Waiting for messages...", _queueName);

            // Block until cancellation.
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public void Dispose()
        {
            if (_channel != null)
            {
                try { _channel?.Close(); } catch { }
                _channel?.Dispose();
                _channel = null;
            }
        }
    }
}
