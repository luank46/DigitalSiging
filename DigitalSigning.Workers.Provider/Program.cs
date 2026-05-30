using DigitalSigning.Core;
using DigitalSigning.Core.MessageQueue;
using DigitalSigning.Core.MessageQueue.Consumers;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Bind settings
var mongoSettings = new MongoDbSettings();
builder.Configuration.GetSection("MongoDb").Bind(mongoSettings);
builder.Services.AddSingleton(mongoSettings);

var rabbitSettings = new RabbitMqSettings();
builder.Configuration.GetSection("RabbitMq").Bind(rabbitSettings);
builder.Services.AddSingleton(rabbitSettings);

// Register core services via AddDigitalSigningCore
// This registers MongoDB, repositories, services, providers, etc.
builder.Services.AddDigitalSigningCore(builder.Configuration);

// Register RabbitMQ — connection, publisher, topology initializer
builder.Services.AddSingleton<RabbitMqConnection>();
builder.Services.AddSingleton<IMessagePublisher, MessagePublisher>();
builder.Services.AddSingleton<IRabbitMqTopologyInitializer, RabbitMqTopologyInitializer>();

// Register consumer — listens to the Provider queue
builder.Services.AddSingleton<IMessageConsumer>(sp =>
    new MessageConsumer(
        sp.GetRequiredService<ILogger<MessageConsumer>>(),
        sp.GetRequiredService<RabbitMqConnection>(),
        RabbitMqTopologyInitializer.ProviderQueue));

// Register file lock service
builder.Services.AddSingleton<FileLockService>();

// Register the worker
builder.Services.AddHostedService<DigitalSigning.Workers.Provider.Worker>();

var host = builder.Build();
host.Run();
