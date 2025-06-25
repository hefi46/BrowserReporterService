using System.Security.Principal;

namespace BrowserReporterService.Services
{
    public class AuthorizationService
    {
        private readonly Serilog.ILogger _logger;
        private readonly UserInfoService _userInfoService;

        public AuthorizationService(Serilog.ILogger logger)
        {
            _logger = logger;
            _userInfoService = new UserInfoService(logger);
        }

        /// <summary>
        /// Checks if the current user should be monitored based on the configuration.
        /// </summary>
        /// <param name="config">Application configuration</param>
        /// <returns>True if the user should be monitored, false otherwise</returns>
        public bool ShouldMonitorCurrentUser(AppConfig config)
        {
            try
            {
                var currentUser = Environment.UserName;
                _logger.Information("Checking monitoring authorization for user: {Username}", currentUser);

                // Check if user is in the monitored users list
                if (config.MonitoredUsers != null && config.MonitoredUsers.Contains(currentUser, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.Information("User {Username} found in monitored users list", currentUser);
                    return true;
                }

                // Check if user is in the monitored group
                if (!string.IsNullOrEmpty(config.MonitoredUsersGroup))
                {
                    if (_userInfoService.IsUserInGroup(config.MonitoredUsersGroup))
                    {
                        _logger.Information("User {Username} is member of monitored group: {Group}", currentUser, config.MonitoredUsersGroup);
                        return true;
                    }
                }

                _logger.Information("User {Username} is not authorized for monitoring", currentUser);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking user authorization");
                return false;
            }
        }

        /// <summary>
        /// Checks if monitoring should be active during the current time.
        /// </summary>
        /// <param name="config">Application configuration</param>
        /// <returns>True if monitoring should be active, false otherwise</returns>
        public bool IsMonitoringTimeActive(AppConfig config)
        {
            try
            {
                var now = DateTime.Now.TimeOfDay;
                
                if (!TimeSpan.TryParse(config.MonitoredHours.Start, out var startTime) ||
                    !TimeSpan.TryParse(config.MonitoredHours.End, out var endTime))
                {
                    _logger.Warning("Invalid monitoring hours format. Start: {Start}, End: {End}", 
                        config.MonitoredHours.Start, config.MonitoredHours.End);
                    return true; // Default to always active if invalid format
                }

                bool isActive;
                if (startTime <= endTime)
                {
                    // Normal case: start and end are on the same day
                    isActive = now >= startTime && now <= endTime;
                }
                else
                {
                    // Overnight case: monitoring spans midnight
                    isActive = now >= startTime || now <= endTime;
                }

                _logger.Debug("Monitoring time check: Current={Current}, Start={Start}, End={End}, Active={Active}",
                    now, startTime, endTime, isActive);
                
                return isActive;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking monitoring time");
                return true; // Default to active if error
            }
        }

        /// <summary>
        /// Checks if a browser should be monitored based on configuration.
        /// </summary>
        /// <param name="config">Application configuration</param>
        /// <param name="browserName">Browser name (chrome, edge, etc.)</param>
        /// <returns>True if the browser should be monitored</returns>
        public bool ShouldMonitorBrowser(AppConfig config, string browserName)
        {
            if (config.Browsers == null || config.Browsers.Count == 0)
            {
                return true; // Default to monitoring all browsers if not specified
            }

            return config.Browsers.Contains(browserName, StringComparer.OrdinalIgnoreCase);
        }
    }
} 