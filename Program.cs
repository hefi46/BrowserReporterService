using BrowserReporterService.Services;
using Serilog;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace BrowserReporterService
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Explicitly initialize the SQLite provider. This is essential for single-file executables.
            Batteries.Init();

            var commandLineArgs = new CommandLineArgs(args);

            if (commandLineArgs.IsDebug)
            {
                AllocConsole();
            }

            var logger = LoggingService.CreateLogger(commandLineArgs);
            Log.Logger = logger;

            if (!commandLineArgs.ShouldRunApplication)
            {
                logger.Information("Running command-line utility.");
                HandleUtilityCommands(commandLineArgs, logger);
            }
            else
            {
                logger.Information("Starting headless browser reporter.");
                RunApplicationAsync(logger, commandLineArgs).GetAwaiter().GetResult();
            }

            logger.Information("Application shutting down.");
            Log.CloseAndFlush();
            if (commandLineArgs.IsDebug)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                FreeConsole();
            }
        }

        private static void HandleUtilityCommands(CommandLineArgs commandLineArgs, ILogger logger)
        {
            try
            {
                if (commandLineArgs.Install)
                {
                    logger.Information("Running --install command.");
                    var taskService = new ScheduledTaskService();
                    taskService.Install();
                }
                else if (commandLineArgs.Uninstall)
                {
                    logger.Information("Running --uninstall command.");
                    var taskService = new ScheduledTaskService();
                    taskService.Uninstall();
                }
                else if (commandLineArgs.EncryptConfig)
                {
                    logger.Information("Running --encryptconfig command.");
                    var configService = new ConfigService(logger);
                    configService.EncryptAndOutputConfig(commandLineArgs.ConfigPath!);
                }
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "A fatal error occurred during command execution.");
                Console.WriteLine($"A fatal error occurred: {ex.Message}");
            }
        }

        private static async Task RunApplicationAsync(ILogger logger, CommandLineArgs args)
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                ApplicationConfiguration.Initialize();
                Application.Run(new HeadlessApplicationContext(logger, args));
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "A fatal error occurred during application execution.");
            }
        }
    }

    public class HeadlessApplicationContext : ApplicationContext
    {
        private readonly Serilog.ILogger _logger;
        private readonly CommandLineArgs _args;
        private readonly ConfigService _configService;
        private readonly UserInfoService _userInfoService;
        private AppConfig? _appConfig;
        private System.Threading.Timer? _syncTimer;
        private readonly Random _jitter = new();
        private bool _isSyncing = false;
        private readonly object _syncLock = new object();

        public HeadlessApplicationContext(Serilog.ILogger logger, CommandLineArgs args)
        {
            _logger = logger;
            _args = args;
            _configService = new ConfigService(_logger);
            _userInfoService = new UserInfoService(_logger);

            _logger.Information("Running in headless mode.");
            _ = StartMainLoopAsync();
        }

        private async Task StartMainLoopAsync()
        {
            _logger.Information("Attempting initial configuration load.");

            _appConfig = await _configService.GetConfigAsync(_args.ConfigPath);

            if (_appConfig == null)
            {
                _logger.Error("Initial configuration load failed. Will retry in 5 minutes.");
                var retryTimer = new System.Threading.Timer(
                    async _ => await RetryConfigLoad(),
                    null,
                    TimeSpan.FromSeconds(300),
                    Timeout.InfiniteTimeSpan);
                return;
            }

            // Apply CLI overrides if present
            if (!string.IsNullOrWhiteSpace(_args.ServerUrl))
            {
                _logger.Information("Overriding server URL from CLI argument: {ServerUrl}", _args.ServerUrl);
                _appConfig.ServerUrl = _args.ServerUrl;
            }

            // Update logging with config settings
            var newLogger = LoggingService.CreateLogger(_args);
            Log.Logger = newLogger;
            _logger.Information("Configuration loaded successfully. Logger re-initialized with remote settings.");

            if (_args.IsOnce)
            {
                _logger.Information("Running a single data sync due to --once flag.");
                await PerformSync();
                Application.Exit();
                return;
            }

            // Start the main randomized sync timer loop
            ScheduleNextSync();
        }

        private async Task RetryConfigLoad()
        {
            _logger.Information("Retrying configuration load...");
            _appConfig = await _configService.GetConfigAsync(_args.ConfigPath);
            if (_appConfig != null)
            {
                _logger.Information("Successfully loaded configuration on retry.");
                await StartMainLoopAsync();
            }
            else
            {
                _logger.Warning("Failed to load configuration on retry. Will try again in 5 minutes.");
                var retryTimer = new System.Threading.Timer(
                    async _ => await RetryConfigLoad(),
                    null,
                    TimeSpan.FromSeconds(300),
                    Timeout.InfiniteTimeSpan);
            }
        }

        private void ScheduleNextSync()
        {
            if (_appConfig == null) return;

            var interval = _appConfig.SyncIntervalMinutes * 60 * 1000;
            var jitterMilliseconds = (int)(_jitter.NextDouble() * (interval * 0.6) - (interval * 0.3)); // +/- 30%
            var dueTime = interval + jitterMilliseconds;

            _logger.Information("Scheduling next sync in {Minutes} minutes ({Jitter}ms jitter).", dueTime / 60000, jitterMilliseconds);

            _syncTimer?.Dispose();
            _syncTimer = new System.Threading.Timer(
                async _ => await PerformSync(),
                null,
                TimeSpan.FromMilliseconds(dueTime),
                Timeout.InfiniteTimeSpan
            );
        }

        private async Task PerformSync()
        {
            lock (_syncLock)
            {
                if (_isSyncing)
                {
                    _logger.Warning("Sync is already in progress. Skipping this cycle.");
                    return;
                }
                _isSyncing = true;
            }

            try
            {
                if (_appConfig == null)
                {
                    _logger.Error("Sync cannot run because configuration is not loaded.");
                    return;
                }

                _logger.Information("Starting data synchronization.");

                // 1. Check user authorization
                var authService = new AuthorizationService(_logger);
                if (!authService.ShouldMonitorCurrentUser(_appConfig))
                {
                    _logger.Warning("User is not authorized for monitoring. Sync cycle will not send data.");
                    return;
                }

                // 2. Check if monitoring is active during current time
                if (!authService.IsMonitoringTimeActive(_appConfig))
                {
                    _logger.Information("Monitoring is not active during current time. Sync cycle will not send data.");
                    return;
                }

                // 3. Scan browsers
                var scanner = new BrowserScannerService(_logger, _appConfig, authService);
                var allVisits = await scanner.ScanAllBrowsersAsync();

                // 4. Filter against cache
                using var cache = new CacheService(_logger);
                cache.PruneOldEntries();
                var sentKeys = cache.GetSentItemKeys();
                var newVisits = allVisits.Where(v => !sentKeys.Contains(v.CompositeKey)).ToList();
                _logger.Information("Found {NewCount} new visits after filtering against the cache.", newVisits.Count);

                // 5. Send to server in batches
                var apiClient = new ApiClient(_logger, _appConfig);
                var overallSuccess = await SendVisitsInBatches(apiClient, newVisits, cache);

                if (overallSuccess)
                {
                    _logger.Information("Data synchronization finished successfully.");
                }
                else
                {
                    _logger.Error("Data synchronization failed during batch sending to the API.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A critical error occurred during the sync process.");
            }
            finally
            {
                lock (_syncLock)
                {
                    _isSyncing = false;
                }
                if (!_args.IsOnce)
                {
                    ScheduleNextSync();
                }
            }
        }

        private async Task<bool> SendVisitsInBatches(ApiClient apiClient, List<BrowserVisit> visits, CacheService cache)
        {
            const int batchSize = 500;
            for (int i = 0; i < visits.Count; i += batchSize)
            {
                var batch = visits.Skip(i).Take(batchSize).ToList();
                _logger.Information("Sending batch {BatchNum} of {TotalBatches} with {Count} items.", (i / batchSize) + 1, (int)Math.Ceiling((double)visits.Count / batchSize), batch.Count);

                var payload = new ReportPayload
                {
                    Username = Environment.UserName,
                    Visits = batch,
                    UserInfo = _userInfoService.GetCurrentUserInfo()
                };

                bool success = await apiClient.SendReportAsync(payload);
                if (success)
                {
                    cache.AddSentItems(batch.Select(v => v.CompositeKey));
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
