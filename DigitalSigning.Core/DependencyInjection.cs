using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DigitalSigning.Core.MongoDB;
using DigitalSigning.Core.Repositories;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Settings;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.Waiting.Config;
using DigitalSigning.Core.Waiting.Interfaces;
using DigitalSigning.Core.Waiting.Services;
using DigitalSigning.Core.Interfaces;
using DigitalSigning.Core.Protection;
using DigitalSigning.Core.Observability.Tracing;
using DigitalSigning.Core.Observability.Metrics;
using DigitalSigning.Core.Observability.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using StackExchange.Redis;


namespace DigitalSigning.Core
{
    /// <summary>
    /// Extension method to register core services into the DI container.
    /// </summary>
    public static partial class DependencyInjection
    {
        public static IServiceCollection AddDigitalSigningCore(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind settings
            var mongoSettings = new MongoDbSettings();
            configuration.GetSection("MongoDb").Bind(mongoSettings);
            services.AddSingleton(mongoSettings);

            var waitingOptions = new WaitingOptions();
            configuration.GetSection("Waiting").Bind(waitingOptions);
            services.AddSingleton(waitingOptions);

            // Register MongoDB context
            services.AddSingleton<MongoDbContext>();

            // Register repositories
            services.AddScoped(typeof(IMongoRepository<>), typeof(MongoRepository<>));
            services.AddScoped<ITransactionRepository, TransactionRepository>();
            services.AddScoped<ITransactionEventRepository, TransactionEventRepository>();
            services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
            services.AddScoped<IDlqRepository, DlqRepository>();

            // Register services
            services.AddScoped<ITransactionService, TransactionService>();
            services.AddScoped<IIdempotencyService, IdempotencyService>();

            // ---- Waiting module --------------------------------------------------
            services.AddSingleton<IWaitingScheduler, WaitingSchedulerService>();
            services.AddSingleton<IWaitingLockService, WaitingLockService>();

            // Register file storage and download
            services.AddSingleton<IGridFsService, GridFsService>();
            services.AddSingleton<IFileDownloader, FileDownloader>();

            // Register PDF signing service (PAdES byte-range signing with PdfPig + .NET built-in)
            services.AddScoped<Services.Pdf.IPdfSigningService, Services.Pdf.PdfSigningService>();

            // Register provider settings from appsettings.json section "Providers"
            services.Configure<Settings.ProviderSettings>(configuration.GetSection("Providers"));

            // ---- Provider services --------------------------------------------------
            services.AddHttpClient<Providers.Vnpt.VnptProviderService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddHttpClient<Providers.Viettel.ViettelProviderService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddHttpClient<Providers.Bkav.BkavProviderService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddHttpClient<Providers.Gcc.GccProviderService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddHttpClient<Providers.Misa.MisaProviderService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            // Viettel auth state service (for SAD/token storage in Redis)
            services.AddSingleton<Providers.Viettel.IViettelAuthStateService, Providers.Viettel.ViettelAuthStateService>();

            // Provider factory (resolves providers by type or name)
            services.AddSingleton<Providers.IProviderFactory, Providers.ProviderFactory>();

            // ---- Protection policies --------------------------------------------------
            // Bulkhead policies with specific concurrency limits
            services.AddSingleton<IBulkheadPolicy<HttpResponseMessage>>(sp =>
                new SemaphoreBulkheadPolicy<HttpResponseMessage>(maxConcurrency: 10));
            services.AddSingleton<IBulkheadPolicy<QueueMessage>>(sp =>
                new SemaphoreBulkheadPolicy<QueueMessage>(maxConcurrency: 20));

            // Circuit‑breaker
            services.AddSingleton<ICircuitBreaker, CircuitBreaker>();

            // Rate‑limiter (token‑bucket based)
            services.AddSingleton<ProviderRateLimiter>(sp =>
                new ProviderRateLimiter(capacity: 200, refillRatePerSec: 100));
            services.AddSingleton<TenantRateLimiter>(sp =>
                new TenantRateLimiter(capacity: 50, refillRatePerSec: 30));
            services.AddSingleton<RateLimiter>(sp => new RateLimiter(
                sp.GetRequiredService<ProviderRateLimiter>(),
                sp.GetRequiredService<TenantRateLimiter>()));

            return services;
        }

        /// <summary>
        /// Register observability services (tracing, metrics, logging, health checks).
        /// </summary>
        public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService("DigitalSigning", serviceVersion: "1.0.0")
                .AddEnvironmentVariableDetector();

            // ---- Redis connection (used by health checks and rate limiters) ----------------
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var redisSettings = new RedisSettings();
                configuration.GetSection("Redis").Bind(redisSettings);
                return ConnectionMultiplexer.Connect(redisSettings.ConnectionString);
            });

            // ---- OpenTelemetry (Tracing + Metrics) ---------------------------------------
            var otlpEndpoint = configuration.GetValue<string>("Otlp:Endpoint");

            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(ActivitySourceExtensions.ActivitySource.Name)
                        .SetSampler(new AlwaysOnSampler())
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            var healthPath = "/health";
                            options.Filter = ctx =>
                            {
                                var path = ctx.Request.Path.Value;
                                return path == null || !path.StartsWith(healthPath, StringComparison.OrdinalIgnoreCase);
                            };
                        })
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();

                    // Add OTLP exporter if endpoint is configured
                    if (!string.IsNullOrEmpty(otlpEndpoint))
                    {
                        builder.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                            var headers = configuration.GetValue<string>("Otlp:Headers");
                            if (!string.IsNullOrEmpty(headers))
                                options.Headers = headers;
                        });
                    }
                })
                .WithMetrics(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(PrometheusMetrics.Meter.Name)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();

                    // Add OTLP exporter if endpoint is configured
                    if (!string.IsNullOrEmpty(otlpEndpoint))
                    {
                        builder.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                            var headers = configuration.GetValue<string>("Otlp:Headers");
                            if (!string.IsNullOrEmpty(headers))
                                options.Headers = headers;
                        });
                    }
                });

            // ---- Metrics Hosted Service ---------------------------------------------------
            services.AddHostedService<MetricsCollector>();

            // ---- Serilog Logging --------------------------------------------------------
            // Logging is configured in Program.cs via UseDigitalSigningSerilog()

            // ---- Health Checks ----------------------------------------------------------
            services.AddSingleton<RedisHealthCheck>();

            return services;
        }
    }
}