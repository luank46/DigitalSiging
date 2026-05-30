using System;
using DigitalSigning.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Implementation of <see cref="ILoggingService"/> that wraps <see cref="ILogger{T}"/>.
    /// </summary>
    public class LoggingService : ILoggingService
    {
        private readonly ILogger _logger;

        public LoggingService(ILogger<LoggingService> logger)
        {
            _logger = logger;
        }

        public void Info(string message, params object?[]? args)
        {
            _logger.LogInformation(message, args);
        }

        public void Error(string message, params object?[]? args)
        {
            _logger.LogError(message, args);
        }

        public void Debug(string message, params object?[]? args)
        {
            _logger.LogDebug(message, args);
        }
    }
}
