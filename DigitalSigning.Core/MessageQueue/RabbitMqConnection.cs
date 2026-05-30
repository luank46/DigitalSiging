using System;
using RabbitMQ.Client;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.MessageQueue
{
    /// <summary>
    /// Manages a persistent RabbitMQ connection.
    /// </summary>
    public class RabbitMqConnection : IDisposable
    {
        private readonly Lazy<IConnection> _connection;

        public RabbitMqConnection(RabbitMqSettings settings)
        {
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
            _connection = new Lazy<IConnection>(() => factory.CreateConnection());
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