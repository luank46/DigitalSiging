using System.Threading.Tasks;

namespace DigitalSigning.Core.MessageQueue.Interfaces
{
    /// <summary>
    /// Publishes domain messages to the appropriate RabbitMQ queue/exchange.
    /// </summary>
    public interface IMessagePublisher
    {
        /// <summary>
        /// Publishes a domain message to the corresponding queue based on its Step.
        /// </summary>
        Task PublishAsync(object message);

        /// <summary>
        /// Publishes a message to a specific queue (for retry scenarios).
        /// </summary>
        Task PublishToQueueAsync(string queueName, object message);
    }
}