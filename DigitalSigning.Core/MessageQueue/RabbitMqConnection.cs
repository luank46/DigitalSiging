using System;
using System.Linq;
using RabbitMQ.Client;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.MessageQueue
{
    /// <summary>
    /// Manages a persistent RabbitMQ connection.
    /// Supports:
    ///   - Cluster connection strings: "host1,host2,host3"
    ///   - Automatic recovery + topology recovery
    ///   - Heartbeat 60s (phát hiện sớm connection loss)
    /// </summary>
    public class RabbitMqConnection : IDisposable
    {
        private readonly Lazy<IConnection> _connection;
        private readonly RabbitMqSettings _settings;

        public RabbitMqConnection(RabbitMqSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var factory = new ConnectionFactory
            {
                UserName = settings.UserName,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,          // Tự động recover topology
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                RequestedHeartbeat = TimeSpan.FromSeconds(30), // Phát hiện connection loss
                ContinuationTimeout = TimeSpan.FromSeconds(20),
            };

            // Hỗ trợ cluster: "host1:5672,host2:5672,host3:5672"
            if (!string.IsNullOrEmpty(settings.Host) && settings.Host.Contains(','))
            {
                var endpoints = settings.Host.Split(',')
                    .Select(h => h.Contains(':')
                        ? new AmqpTcpEndpoint(h.Split(':')[0], int.Parse(h.Split(':')[1]))
                        : new AmqpTcpEndpoint(h, settings.Port))
                    .ToArray();
                _connection = new Lazy<IConnection>(() => factory.CreateConnection(endpoints));
            }
            else
            {
                factory.HostName = settings.Host;
                factory.Port = settings.Port;
                _connection = new Lazy<IConnection>(() => factory.CreateConnection());
            }
        }

        public IConnection Connection => _connection.Value;

        public bool IsConnected => _connection.IsValueCreated && _connection.Value.IsOpen;

        public IModel CreateChannel()
        {
            return Connection.CreateModel();
        }

        public void Dispose()
        {
            if (_connection.IsValueCreated && _connection.Value.IsOpen)
            {
                _connection.Value.Close();
                _connection.Value.Dispose();
            }
        }
    }
}
