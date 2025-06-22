using Microsoft.Data.Sqlite;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BrowserReporterService.Services
{
    public class BrowserScannerService
    {
        private readonly Serilog.ILogger _logger;
        private readonly AppConfig _config;
        private readonly AuthorizationService _authService;
        private static readonly long WebkitEpochDelta = 11644473600000;

        public BrowserScannerService(Serilog.ILogger logger, AppConfig config, AuthorizationService authService)
        {
            _logger = logger;
            _config = config;
            _authService = authService;
        }

        public async Task<List<BrowserVisit>> ScanAllBrowsersAsync()
        {
            // Check if current user should be monitored
            if (!_authService.ShouldMonitorCurrentUser(_config))
            {
                _logger.Information("Current user is not authorized for monitoring. Skipping browser scan.");
                return new List<BrowserVisit>();
            }

            // Check if monitoring is active during current time
            if (!_authService.IsMonitoringTimeActive(_config))
            {
                _logger.Information("Monitoring is not active during current time. Skipping browser scan.");
                return new List<BrowserVisit>();
            }

            var allVisits = new List<BrowserVisit>();
            var computerName = Environment.MachineName;

            var tasks = new List<Task<List<BrowserVisit>>>();

            // Only scan browsers that are configured to be monitored
            if (_authService.ShouldMonitorBrowser(_config, "chrome"))
            {
                _logger.Information("Chrome monitoring enabled");
                tasks.Add(ScanBrowserAsync("Chrome", @"Google\Chrome\User Data", computerName));
            }
            else
            {
                _logger.Information("Chrome monitoring disabled by configuration");
            }

            if (_authService.ShouldMonitorBrowser(_config, "edge"))
            {
                _logger.Information("Edge monitoring enabled");
                tasks.Add(ScanBrowserAsync("Edge", @"Microsoft\Edge\User Data", computerName));
            }
            else
            {
                _logger.Information("Edge monitoring disabled by configuration");
            }

            if (tasks.Count == 0)
            {
                _logger.Warning("No browsers are configured for monitoring");
                return allVisits;
            }

            var results = await Task.WhenAll(tasks);
            allVisits.AddRange(results.SelectMany(r => r));
            
            _logger.Information("Completed scan of all browsers. Found {Count} total history items.", allVisits.Count);
            return allVisits;
        }

        private async Task<List<BrowserVisit>> ScanBrowserAsync(string browserType, string profilePath, string computerName)
        {
            var visits = new List<BrowserVisit>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataPath = Path.Combine(localAppData, profilePath);

            if (!Directory.Exists(userDataPath))
            {
                _logger.Warning("{BrowserType} user data path not found: {Path}", browserType, userDataPath);
                return visits;
            }
            
            var profileDirectories = Directory.GetDirectories(userDataPath)
                .Where(d => Path.GetFileName(d).StartsWith("Profile ") || Path.GetFileName(d).Equals("Default"))
                .ToList();

            foreach (var profileDir in profileDirectories)
            {
                var historyDbPath = Path.Combine(profileDir, "History");
                var profileName = Path.GetFileName(profileDir);
                
                if (File.Exists(historyDbPath) && !profileName.Equals("System Profile", StringComparison.OrdinalIgnoreCase) && !profileName.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Information("Scanning {BrowserType} profile '{ProfileName}' at {Path}", browserType, profileName, historyDbPath);
                    visits.AddRange(await QueryHistoryDatabaseAsync(historyDbPath, browserType, profileName, computerName));
                }
            }
            return visits;
        }

        private async Task<List<BrowserVisit>> QueryHistoryDatabaseAsync(string dbPath, string browserType, string profileName, string computerName)
        {
            // Primary Method: Read-only, immutable connection using URI format
            var primaryConnectionString = $"Data Source=file:{dbPath}?mode=ro&immutable=1";
            try
            {
                _logger.Information("Attempting to query history with immutable flag: {Path}", dbPath);
                return await ExecuteHistoryQueryInternalAsync(primaryConnectionString, browserType, profileName, computerName);
            }
            catch (SqliteException ex) when (ex.Message.ToLower().Contains("locked"))
            {
                _logger.Warning(ex, "History database at {Path} is locked, even with immutable flag. Attempting fallback copy method.", dbPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An unexpected error occurred during primary query attempt for {Path}", dbPath);
                return new List<BrowserVisit>();
            }

            // Fallback Method: Copy the database and its WAL files to a temp location.
            string tempDbFile = "";
            try
            {
                _logger.Information("Executing fallback copy method for {Path}", dbPath);
                tempDbFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
                
                File.Copy(dbPath, tempDbFile, true);
                CopyIfExists(dbPath + "-wal", tempDbFile + "-wal");
                CopyIfExists(dbPath + "-shm", tempDbFile + "-shm");
                
                var fallbackConnectionString = $"Data Source={tempDbFile};Mode=ReadOnly;Cache=Shared;";
                return await ExecuteHistoryQueryInternalAsync(fallbackConnectionString, browserType, profileName, computerName);
            }
            catch (Exception fallbackEx)
            {
                _logger.Error(fallbackEx, "Failed to query history database using fallback copy method for {Path}", dbPath);
                return new List<BrowserVisit>();
            }
            finally
            {
                CleanupTempFile(tempDbFile);
                CleanupTempFile(tempDbFile + "-wal");
                CleanupTempFile(tempDbFile + "-shm");
            }
        }
        
        private void CopyIfExists(string source, string dest)
        {
            if (File.Exists(source))
            {
                File.Copy(source, dest, true);
                _logger.Information("Copied temp file from {Source} to {Dest}", source, dest);
            }
        }
        
        private void CleanupTempFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex) { _logger.Warning(ex, "Failed to delete temp file {Path}", path); }
            }
        }

        private async Task<List<BrowserVisit>> ExecuteHistoryQueryInternalAsync(string connectionString, string browserType, string profileName, string computerName)
        {
            var visits = new List<BrowserVisit>();
            // Hardcoded to only fetch the last day of history, per user request.
            var cutoffTime = DateTime.UtcNow.AddDays(-1);
            
            // Chrome WebKit timestamp is microseconds since Jan 1, 1601 UTC.
            var webkitCutoffTime = new DateTimeOffset(cutoffTime).ToUniversalTime().Ticks - new DateTimeOffset(1601,1,1,0,0,0, TimeSpan.Zero).Ticks;
            webkitCutoffTime /= 10; // Convert Ticks (100ns) to microseconds

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT urls.url, urls.title, visits.visit_time, urls.visit_count
                FROM urls JOIN visits ON urls.id = visits.url
                WHERE visits.visit_time > $cutoffTime";
            command.Parameters.AddWithValue("$cutoffTime", webkitCutoffTime);
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var webkitTime = reader.GetInt64(2);
                visits.Add(new BrowserVisit
                {
                    Url = reader.GetString(0),
                    Title = reader.GetString(1),
                    VisitTime = (webkitTime / 1000) - WebkitEpochDelta,
                    Duration = 0, // Duration not available from Chrome history DB
                    Browser = browserType.ToLower(), // Server expects lowercase browser name
                    VisitCount = reader.GetInt32(3),
                    BrowserType = browserType,
                    ProfileName = profileName,
                    ComputerName = computerName
                });
            }
            return visits;
        }
    }
} 