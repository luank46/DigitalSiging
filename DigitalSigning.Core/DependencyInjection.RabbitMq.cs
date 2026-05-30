using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DigitalSigning.Core.MessageQueue;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core
{
    public static partial class DependencyInjection
    {
        /// <summary>
        /// Register RabbitMQ related services (connection, topology, publisher, consumer).
        /// </summary>
        public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
        {
            var rabbitSettings = new RabbitMqSettings();
            configuration.GetSection("RabbitMq").Bind(rabbitSettings);
            services.AddSingleton(rabbitSettings);

            // Single connection per app lifetime
            services.AddSingleton<RabbitMqConnection>();

            // Topology initializer – creates exchanges/queues once at startup
            services.AddSingleton<IRabbitMqTopologyInitializer, RabbitMqTopologyInitializer>();

            // Publisher used by workers and API
            services.AddSingleton<IMessagePublisher, MessagePublisher>();

            return services;
        }
    }
}