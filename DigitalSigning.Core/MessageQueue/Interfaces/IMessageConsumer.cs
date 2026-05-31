using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.MessageQueue.Interfaces
{
    /// <summary>
    /// Defines the contract for a message consumer that processes <see cref="QueueMessage"/>
    /// from a specific RabbitMQ queue.
    /// </summary>
    public interface IMessageConsumer
    {
        /// <summary>
        /// The name of the queue this consumer listens to.
        /// </summary>
        string QueueName { get; }

        /// <summary>
        /// Starts consuming messages and dispatching each deserialized <see cref="QueueMessage"/>
        /// to the provided <paramref name="handler"/>.
        /// Blocks until <paramref name="cancellationToken"/> is cancelled or the connection is lost.
        /// </summary>
        /// <param name="handler">Async callback invoked for each message. On exception the message is nacked.</param>
        /// <param name="cancellationToken">Signals graceful shutdown.</param>
        Task StartConsumingAsync(Func<QueueMessage, CancellationToken, Task> handler, CancellationToken cancellationToken);
    }
}