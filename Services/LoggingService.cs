using Serilog;
using Serilog.Core;

namespace BrowserReporterService.Services
{
    public static class LoggingService
    {
        public static Logger CreateLogger(CommandLineArgs args)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(appDataPath, "BrowserReporter");
            var logFilePath = Path.Combine(logDirectory, "logs.txt");

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day, // Fallback, size limit is primary
                    fileSizeLimitBytes: 5 * 1024 * 1024, // 5 MB
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 3, // keeps logs.txt, logs.txt.1, logs.txt.2
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));

            if (args.IsDebug)
            {
                loggerConfiguration.WriteTo.Console();
            }

            return loggerConfiguration.CreateLogger();
        }

        public static Logger CreateConsoleLogger()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(appDataPath, "BrowserReporter");
            var logFilePath = Path.Combine(logDirectory, "logs.txt");

            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 5 * 1024 * 1024, // 5 MB
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 3,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }
} 