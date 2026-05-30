using DigitalSigning.Api.Middleware;
using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Core;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.MessageQueue;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Observability.Logging;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Register DigitalSigning Core services (MongoDB, Redis, RabbitMQ, Idempotency, etc.)
builder.Services.AddDigitalSigningCore(builder.Configuration);
builder.Services.AddRabbitMq(builder.Configuration);

// Register Observability (tracing, metrics, health checks)
builder.Services.AddObservability(builder.Configuration);

// Configure Serilog as the logging provider
builder.Host.UseDigitalSigningSerilog();

// Register file-level distributed lock service
builder.Services.AddSingleton<FileLockService>();

// Register legacy services (AccountService, RedisCacheService)
builder.Services.AddSingleton<IAccountService, AccountService>();
builder.Services.AddSingleton<IAsyncCacheService>(sp =>
    new RedisCacheService(
        sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
        sp.GetRequiredService<ILogger<RedisCacheService>>(),
        builder.Configuration.GetSection("Redis")["InstanceName"] ?? "RemoteSigning_"));

// Register Monitoring (InfluxDB)
builder.Services.Configure<DigitalSigning.Core.Services.Monitoring.InfluxSettings>(
    builder.Configuration.GetSection("InfluxSettings"));
builder.Services.AddSingleton<DigitalSigning.Core.Services.Monitoring.IMonitoringService,
    DigitalSigning.Core.Services.Monitoring.InfluxDbService>();

// Register controllers
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Custom validation response
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(x => x.ErrorMessage))
                .ToList();

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new ApiErrorResponse
            {
                ErrorCode = ErrorCode.ValidationError.ToString(),
                Message = "Validation failed",
                Details = string.Join("; ", errors),
                TraceId = System.Diagnostics.Activity.Current?.Id
                         ?? context.HttpContext.TraceIdentifier
            });
        };
    });

// OpenAPI / Swagger (development only)
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DigitalSigning API",
        Version = "v1",
        Description = "Hệ thống ký số từ xa - Backward compatible với legacy API"
    });
    options.OperationFilter<SwaggerAddKeyHeader>();
});



// CORS — restrict origins from config in production, allow all in development
var corsOrigins = builder.Configuration.GetValue<string>("Cors:Origins") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins == "*")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            var origins = corsOrigins.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Health checks
// Health checks - include Redis health check
builder.Services.AddHealthChecks()
    .AddCheck<DigitalSigning.Core.HealthChecks.MongoDbHealthCheck>("mongodb")
    .AddCheck<DigitalSigning.Core.HealthChecks.RabbitMqHealthCheck>("rabbitmq")
    .AddCheck<DigitalSigning.Core.Observability.HealthChecks.RedisHealthCheck>("redis");

var app = builder.Build();

// ------------ Middleware pipeline ------------

// Global exception handler
app.UseGlobalException();

// Swagger (development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// CORS
app.UseCors();

// HTTPS redirect (production)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Routing
app.UseRouting();

// Map controllers
app.MapControllers();

// Healthcheck endpoint
app.MapHealthChecks("/health");

// Warm up RabbitMQ topology at startup
using (var scope = app.Services.CreateScope())
{
    var topoInit = scope.ServiceProvider.GetRequiredService<IRabbitMqTopologyInitializer>();
    await topoInit.InitializeAsync();
}

app.Run();
