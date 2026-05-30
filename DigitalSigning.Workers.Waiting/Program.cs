using DigitalSigning.Core;
using DigitalSigning.Core.MessageQueue;
using DigitalSigning.Core.MessageQueue.Consumers;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Settings;
using DigitalSigning.Core.Waiting.Config;
using DigitalSigning.Core.Waiting.Interfaces;
using DigitalSigning.Core.Waiting.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Bind settings
var mongoSettings = new MongoDbSettings();
builder.Configuration.GetSection("MongoDb").Bind(mongoSettings);
builder.Services.AddSingleton(mongoSettings);

var rabbitSettings = new RabbitMqSettings();
builder.Configuration.GetSection("RabbitMq").Bind(rabbitSettings);
builder.Services.AddSingleton(rabbitSettings);

var redisSettings = new RedisSettings();
builder.Configuration.GetSection("Redis").Bind(redisSettings);
builder.Services.AddSingleton(redisSettings);

var waitingOptions = new WaitingOptions();
builder.Configuration.GetSection("Waiting").Bind(waitingOptions);
builder.Services.AddSingleton(waitingOptions);

// Register core services via AddDigitalSigningCore
builder.Services.AddDigitalSigningCore(builder.Configuration);

// Register RabbitMQ — connection, publisher, topology initializer
builder.Services.AddSingleton<RabbitMqConnection>();
builder.Services.AddSingleton<IMessagePublisher, MessagePublisher>();
builder.Services.AddSingleton<IRabbitMqTopologyInitializer, RabbitMqTopologyInitializer>();

// Register Waiting infrastructure (overrides waiting registrations from AddDigitalSigningCore)
builder.Services.AddSingleton<IWaitingScheduler, WaitingSchedulerService>();
builder.Services.AddSingleton<IWaitingLockService, WaitingLockService>();

// Register the waiting worker (polls Redis sorted set, re-publishes to Provider queue)
builder.Services.AddHostedService<DigitalSigning.Workers.Waiting.Worker>();

var host = builder.Build();
host.Run();
