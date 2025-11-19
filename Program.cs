using BrowserReporterService.Services;
using Serilog;
using System.Diagnostics;
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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

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
                logger.Information("Starting tray application.");
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
                Application.Run(new TrayApplicationContext(logger, args));
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "A fatal error occurred during application execution.");
            }
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly Serilog.ILogger _logger;
        private readonly CommandLineArgs _args;
        private readonly ConfigService _configService;
        private readonly UserInfoService _userInfoService;
        private AppConfig? _appConfig;
        private System.Threading.Timer? _syncTimer;
        private readonly Random _jitter = new();
        private bool _isSyncing = false;
        private object _syncLock = new object();
        private bool _consoleAllocated = false;

        public TrayApplicationContext(Serilog.ILogger logger, CommandLineArgs args)
        {
            _logger = logger;
            _args = args;
            _configService = new ConfigService(_logger);
            _userInfoService = new UserInfoService(_logger);

            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Status: Initializing...", null, (s, e) => { });
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Force Data Sync Now", null, OnForceSync);
            contextMenu.Items.Add("Show Debug Console", null, OnViewLogs);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit (Password Protected)", null, OnExit);
            contextMenu.Items[0].Enabled = false;

            trayIcon = new NotifyIcon()
            {
                Icon = new Icon(LoadEmbeddedResource("icon_grey.ico")),
                ContextMenuStrip = contextMenu,
                Visible = !_args.NoTray,
                Text = "Browser Reporter"
            };

            if (_args.NoTray)
            {
                _logger.Information("Running in no-tray mode. System tray icon will not be shown.");
            }
            else
            {
                _logger.Information("Tray icon and context menu initialized.");
            }
            _ = StartMainLoopAsync();
        }

        private async Task StartMainLoopAsync()
        {
            _logger.Information("Attempting initial configuration load.");
            SetIconAndStatus(IconColor.Grey, "Initializing...");

            _appConfig = await _configService.GetConfigAsync(_args.ConfigPath);

            if (_appConfig == null)
            {
                SetIconAndStatus(IconColor.Red, "Error: Config failed");
                // Implement retry logic for config download
                var retryTimer = new System.Threading.Timer(
                    async _ => await RetryConfigLoad(),
                    null,
                    TimeSpan.FromSeconds(300), // Default, will be updated if config ever loads
                    Timeout.InfiniteTimeSpan);
                return;
            }

            // If config loaded, apply CLI overrides if present
            if (!string.IsNullOrWhiteSpace(_args.ServerUrl))
            {
                _logger.Information("Overriding server URL from CLI argument: {ServerUrl}", _args.ServerUrl);
                _appConfig.ServerUrl = _args.ServerUrl;
            }

            // If config loaded, update logging with new settings
            var newLogger = LoggingService.CreateLogger(_args); // Re-create logger with potential new settings from config
            Log.Logger = newLogger;
            _logger.Information("Configuration loaded successfully. Logger re-initialized with remote settings.");

            if (_args.IsOnce)
            {
                _logger.Information("Running a single data sync due to --once flag.");
                await PerformSync();
                OnExit(null, EventArgs.Empty);
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
                await StartMainLoopAsync(); // Re-run the main startup sequence
            }
            else
            {
                _logger.Warning("Failed to load configuration on retry. Will try again later.");
                var retryInterval = 300;
                var retryTimer = new System.Threading.Timer(
                    async _ => await RetryConfigLoad(),
                    null,
                    TimeSpan.FromSeconds(retryInterval), 
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
                Timeout.InfiniteTimeSpan // Ensures it only runs once per scheduled time
            );
        }

        private async Task PerformSync()
        {
            lock(_syncLock)
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
                    SetIconAndStatus(IconColor.Red, "Error: No Config");
                    return;
                }

                _logger.Information("Starting data synchronization.");
                SetIconAndStatus(IconColor.Grey, "Syncing...");

                // 1. Check user authorization
                var authService = new AuthorizationService(_logger);
                if (!authService.ShouldMonitorCurrentUser(_appConfig))
                {
                    SetIconAndStatus(IconColor.Yellow, "Connected, Not Reporting");
                    _logger.Warning("User is not authorized for monitoring. Sync cycle will not send data.");
                    return; // Stop the sync process but don't show an error
                }

                // 1.5. Check if monitoring is active during current time
                if (!authService.IsMonitoringTimeActive(_appConfig))
                {
                    SetIconAndStatus(IconColor.Yellow, "Outside Monitoring Hours");
                    _logger.Information("Monitoring is not active during current time. Sync cycle will not send data.");
                    return; // Stop the sync process but don't show an error
                }

                // 2. Scan browsers
                var scanner = new BrowserScannerService(_logger, _appConfig, authService);
                var allVisits = await scanner.ScanAllBrowsersAsync();

                // 3. Filter against cache
                using var cache = new CacheService(_logger);
                var sentKeys = cache.GetSentItemKeys();
                var newVisits = allVisits.Where(v => !sentKeys.Contains(v.CompositeKey)).ToList();
                _logger.Information("Found {NewCount} new visits after filtering against the cache.", newVisits.Count);

                // 4. Send to server in batches
                var apiClient = new ApiClient(_logger, _appConfig);
                var overallSuccess = await SendVisitsInBatches(apiClient, newVisits, cache);

                // 5. Update icon based on overall success
                if (overallSuccess)
                {
                    SetIconAndStatus(IconColor.Green, "Connected & Reporting");
                    _logger.Information("Data synchronization finished successfully.");
                }
                else
                {
                    SetIconAndStatus(IconColor.Red, "Error: API Failed");
                    _logger.Error("Data synchronization failed during batch sending to the API.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A critical error occurred during the sync process.");
                SetIconAndStatus(IconColor.Red, "Error: Sync Failed");
            }
            finally
            {
                lock(_syncLock)
                {
                    _isSyncing = false;
                }
                // Schedule the next run, regardless of outcome.
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
                    // Update the cache immediately after a successful batch
                    cache.AddSentItems(batch.Select(v => v.CompositeKey));
                }
                else
                {
                    // If any batch fails, stop and report the error.
                    // The remaining unsent items will be picked up on the next sync.
                    return false;
                }
            }
            return true;
        }

        private void OnForceSync(object? sender, EventArgs e)
        {
            _logger.Information("'Force Data Sync Now' clicked.");
            _ = PerformSync();
        }

        private void OnViewLogs(object? sender, EventArgs e)
        {
            _logger.Information("'View Logs' clicked. Showing debug console.");

            try
            {
                if (!_consoleAllocated)
                {
                    if (AllocConsole())
                    {
                        _consoleAllocated = true;
                        Console.SetWindowPosition(0, 0);
                        Console.SetWindowSize(120, 30);
                        Console.Title = "Browser Reporter - Debug Console";
                        
                        // Redirect the logger to also output to console
                        var newLogger = LoggingService.CreateConsoleLogger();
                        Log.Logger = newLogger;
                        
                        Console.WriteLine("=== Browser Reporter Debug Console ===");
                        Console.WriteLine($"Started at: {DateTime.Now}");
                        Console.WriteLine($"Status: {contextMenu.Items[0].Text}");
                        Console.WriteLine($"Config loaded: {(_appConfig != null ? "Yes" : "No")}");
                        Console.WriteLine($"User: {Environment.UserName}");
                        Console.WriteLine($"Machine: {Environment.MachineName}");
                        Console.WriteLine("=====================================");
                        Console.WriteLine();
                        
                        _logger.Information("Debug console window opened successfully.");
                    }
                    else
                    {
                        MessageBox.Show("Failed to allocate console window.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        _logger.Warning("Failed to allocate console window.");
                    }
                }
                else
                {
                    // Console already exists, just bring it to front
                    var consoleWindow = GetConsoleWindow();
                    if (consoleWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(consoleWindow);
                        ShowWindow(consoleWindow, SW_RESTORE);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to show debug console.");
                MessageBox.Show($"Could not show debug console: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        async void OnExit(object? sender, EventArgs e)
        {
            _logger.Information("Exit requested from context menu.");
            
            // Show password dialog
            var passwordForm = new Form
            {
                Text = "Browser Reporter - Exit Confirmation",
                Size = new Size(350, 150),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true
            };

            var label = new Label
            {
                Text = "Enter admin password to exit:",
                Location = new Point(20, 20),
                Size = new Size(300, 20)
            };

            var passwordBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(200, 20),
                PasswordChar = '*',
                UseSystemPasswordChar = true
            };

            var okButton = new Button
            {
                Text = "OK",
                Location = new Point(230, 48),
                Size = new Size(60, 25),
                DialogResult = DialogResult.OK
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(230, 78),
                Size = new Size(60, 25),
                DialogResult = DialogResult.Cancel
            };

            passwordForm.Controls.AddRange(new Control[] { label, passwordBox, okButton, cancelButton });
            passwordForm.AcceptButton = okButton;
            passwordForm.CancelButton = cancelButton;

            // Focus on password box
            passwordForm.Load += (s, args) => passwordBox.Focus();

            var result = passwordForm.ShowDialog();
            
            if (result == DialogResult.OK)
            {
                var enteredPassword = passwordBox.Text;
                
                // Check if password is correct (using configured password)
                var expectedPassword = _appConfig?.ExitPassword ?? "BRAdmin2025";
                if (enteredPassword == expectedPassword)
                {
                    _logger.Information("Exit password accepted. Shutting down application.");
                    trayIcon.Visible = false;
                    if (_consoleAllocated)
                    {
                        FreeConsole();
                    }
                    Application.Exit();
                }
                else
                {
                    _logger.Warning("Incorrect exit password entered by user: {User}", Environment.UserName);
                    MessageBox.Show("Incorrect password. Application will continue running.", 
                        "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                _logger.Information("Exit cancelled by user.");
            }
        }

        private enum IconColor { Red, Yellow, Green, Grey }
        private void SetIconAndStatus(IconColor color, string statusText)
        {
            string iconFile = $"icon_{color.ToString().ToLower()}.ico";
            trayIcon.Icon = new Icon(LoadEmbeddedResource(iconFile));
            trayIcon.Text = $"Browser Reporter: {statusText}";
            contextMenu.Items[0].Text = $"Status: {statusText}";
            _logger.Information("Status updated. Color: {Color}, Text: {Status}", color, statusText);
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                // Fallback if we can't determine IP
            }
            return "127.0.0.1";
        }

        private static Stream LoadEmbeddedResource(string resourceName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fullResourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith(resourceName));
            return assembly.GetManifestResourceStream(fullResourceName) ?? throw new Exception($"Could not find resource: {resourceName}");
        }
    }
} 