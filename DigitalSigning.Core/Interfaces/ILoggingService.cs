using System;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Simple logging abstraction.
    /// </summary>
    public interface ILoggingService
    {
        void Info(string message, params object?[]? args);
        void Error(string message, params object?[]? args);
        void Debug(string message, params object?[]? args);
    }
}