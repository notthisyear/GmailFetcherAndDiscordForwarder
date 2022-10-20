using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NLog;

namespace GoogleMailFetcher.Common
{
    internal enum LoggingLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    internal enum LoggerType
    {
        Internal,
        GoogleCommunication
    }

    internal static class LoggingExtensionMethods
    {
        private static readonly Dictionary<LoggerType, ILogger> s_loggers = new()
        {
            { LoggerType.Internal, LogManager.GetLogger($"{LoggerType.Internal}") },
            { LoggerType.GoogleCommunication, LogManager.GetLogger($"{LoggerType.GoogleCommunication}") },
        };

        #region Log methods
        public static void Log(this LoggerType logger, LoggingLevel level, string message)
            => logger.Log(level, exception: default, message: message);

        public static void Log(this LoggerType logger, LoggingLevel level, Exception? exception = default, string message = "", [CallerFilePath] string? caller = null, [CallerLineNumber] int lineNumber = -1)
        {
            lock (s_loggers)
            {
                ILogger currentLogger = s_loggers[logger];
                LogEventInfo logEvent = exception == default ?
                    currentLogger.GetLogEventForLogger(message, level) : currentLogger.GetLogEventForLogger($"{caller}():{lineNumber}|{message}", exception, level);
                currentLogger.Log(logEvent);
            }
        }
        #endregion

        #region Private methods
        private static LogEventInfo GetLogEventForLogger(this ILogger logger, string message, LoggingLevel level)
            => logger != null ? LogEventInfo.Create(level.GetLogLevel(), logger.Name, message) : LogEventInfo.CreateNullEvent();

        private static LogEventInfo GetLogEventForLogger(this ILogger logger, string message, Exception exception, LoggingLevel level)
            => logger != null ? LogEventInfo.Create(level.GetLogLevel(), logger.Name, exception, null, message) : LogEventInfo.CreateNullEvent();

        private static LogLevel GetLogLevel(this LoggingLevel level)
            => level switch
            {
                LoggingLevel.Trace => LogLevel.Trace,
                LoggingLevel.Debug => LogLevel.Debug,
                LoggingLevel.Info => LogLevel.Info,
                LoggingLevel.Warning => LogLevel.Warn,
                LoggingLevel.Error => LogLevel.Error,
                LoggingLevel.Fatal => LogLevel.Fatal,
                _ => throw new NotImplementedException($"No LogLevel matching '{level}' is defined"),
            };
        #endregion
    }
}
