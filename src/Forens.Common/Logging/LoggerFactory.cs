using System;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Forens.Common.Logging
{
    public enum LogVerbosity
    {
        Quiet,
        Normal,
        Verbose
    }

    public static class LoggerFactory
    {
        public static Logger CreateRunLogger(string logFilePath, LogVerbosity verbosity)
        {
            if (string.IsNullOrEmpty(logFilePath))
                throw new ArgumentException("logFilePath is required.", nameof(logFilePath));

            var consoleLevel = verbosity == LogVerbosity.Quiet ? LogEventLevel.Warning
                              : verbosity == LogVerbosity.Verbose ? LogEventLevel.Verbose
                              : LogEventLevel.Information;

            return new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Console(restrictedToMinimumLevel: consoleLevel,
                    outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    formatter: new CompactJsonFormatter(),
                    path: logFilePath,
                    restrictedToMinimumLevel: LogEventLevel.Verbose,
                    shared: false,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();
        }
    }
}
