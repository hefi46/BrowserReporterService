using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Principal;
using System.DirectoryServices.AccountManagement;

namespace BrowserReporterService.Services
{
    public class AuthorizationService
    {
        private readonly Serilog.ILogger _logger;

        public AuthorizationService(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public bool IsCurrentUserAuthorized(AppConfig config)
        {
            if (!config.EnableGroupFiltering || config.SecurityGroups.Length == 0)
            {
                _logger.Information("Group filtering is disabled or no security groups are defined. Authorization check is skipped (authorized by default).");
                return true;
            }

            _logger.Information("Performing authorization check for user: {User}", Environment.UserName);

            if (config.PreferLocalGroupLookup)
            {
                _logger.Information("Attempting authorization using local security token lookup.");
                try
                {
                    if (IsUserInGroupsLocally(config.SecurityGroups))
                    {
                        _logger.Information("User is authorized based on local group membership.");
                        return true;
                    }
                    _logger.Warning("User not found in required groups via local lookup. Falling back to LDAP.");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Local group lookup failed. Falling back to LDAP.");
                }
            }

            _logger.Information("Attempting authorization using LDAP lookup.");
            try
            {
                if (IsUserInGroupsViaLdap(Environment.UserName, config.SecurityGroups, config))
                {
                    _logger.Information("User is authorized based on LDAP group membership.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "LDAP group lookup failed.");
            }

            _logger.Warning("Authorization check failed. User '{User}' is not a member of any required security groups.", Environment.UserName);
            return false;
        }

        private bool IsUserInGroupsLocally(string[] groupSidsOrNames)
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity.Groups == null)
                {
                    _logger.Warning("Could not retrieve user's security groups from Windows identity.");
                    return false;
                }

                var userGroups = identity.Groups.Select(g => {
                    try 
                    {
                        return g.Translate(typeof(NTAccount)).Value;
                    }
                    catch (IdentityNotMappedException)
                    {
                        return g.Value; // Fallback to SID if name can't be resolved
                    }
                }).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var requiredGroup in groupSidsOrNames)
                {
                    if (userGroups.Any(ug => ug.Equals(requiredGroup, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.Information("User is a member of '{Group}' (local check).", requiredGroup);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsUserInGroupsViaLdap(string username, string[] groupDns, AppConfig config)
        {
            try
            {
                using (var connection = new LdapConnection(config.Ldap.Server))
                {
                    connection.AuthType = AuthType.Negotiate;
                    connection.Bind(); // Bind as current user

                    var userSearchRequest = new SearchRequest(
                        config.Ldap.UserSearchBase,
                        $"(&(objectClass=user)(sAMAccountName={username}))",
                        SearchScope.Subtree,
                        "memberOf"
                    );

                    var userResponse = (SearchResponse)connection.SendRequest(userSearchRequest);
                    if (userResponse.Entries.Count == 0)
                    {
                        _logger.Warning("LDAP search found no user with sAMAccountName: {Username}", username);
                        return false;
                    }

                    var userEntry = userResponse.Entries[0];
                    var memberOf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (userEntry.Attributes.Contains("memberOf"))
                    {
                        foreach (string group in userEntry.Attributes["memberOf"].GetValues(typeof(string)))
                        {
                            memberOf.Add(group);
                        }
                    }

                    foreach (var requiredGroupDn in groupDns)
                    {
                        if (memberOf.Contains(requiredGroupDn))
                        {
                            _logger.Information("User is a member of '{Group}' (LDAP check).", requiredGroupDn);
                            return true;
                        }
                    }
                }
            }
            catch (LdapException ex)
            {
                _logger.Error(ex, "An LDAP exception occurred during authorization check.");
                throw;
            }
            return false;
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
                    if (IsUserInGroup(currentUser, config.MonitoredUsersGroup))
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
            if (config.Browsers == null || config.Browsers.Length == 0)
            {
                return true; // Default to monitoring all browsers if not specified
            }

            return config.Browsers.Contains(browserName, StringComparer.OrdinalIgnoreCase);
        }

        private bool IsUserInGroup(string username, string groupName)
        {
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var user = UserPrincipal.FindByIdentity(context, username);
                
                if (user == null)
                {
                    _logger.Warning("User {Username} not found in domain", username);
                    return false;
                }

                using var group = GroupPrincipal.FindByIdentity(context, groupName);
                if (group == null)
                {
                    _logger.Warning("Group {GroupName} not found in domain", groupName);
                    return false;
                }

                return user.IsMemberOf(group);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking group membership for user {Username} in group {GroupName}", username, groupName);
                
                // Fallback: try local group check
                try
                {
                    using var context = new PrincipalContext(ContextType.Machine);
                    using var user = UserPrincipal.FindByIdentity(context, username);
                    using var group = GroupPrincipal.FindByIdentity(context, groupName);
                    
                    return user?.IsMemberOf(group) == true;
                }
                catch (Exception localEx)
                {
                    _logger.Error(localEx, "Error checking local group membership");
                    return false;
                }
            }
        }
    }
} 