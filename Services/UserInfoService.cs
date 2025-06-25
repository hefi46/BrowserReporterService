using System.Security.Principal;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices;

namespace BrowserReporterService.Services
{
    public class UserInfoService
    {
        private readonly Serilog.ILogger _logger;

        public UserInfoService(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public UserInfo GetCurrentUserInfo()
        {
            try
            {
                var username = Environment.UserName;
                var machineName = Environment.MachineName;
                var domainName = Environment.UserDomainName;

                _logger.Information("Getting user info for: {Username}@{Domain} on {Machine}", username, domainName, machineName);

                // Get Active Directory info first
                var adInfo = GetActiveDirectoryUserInfo();

                var userInfo = new UserInfo
                {
                    Username = username,
                    ComputerName = machineName,
                    Domain = domainName,
                    FullName = GetUserFullName(),
                    DisplayName = adInfo.DisplayName,
                    FirstName = adInfo.FirstName,
                    LastName = adInfo.LastName,
                    Department = GetUserDepartment(),
                    Email = adInfo.Email,
                    Groups = GetUserGroups()
                };

                _logger.Information("User info retrieved: DisplayName={DisplayName}, FirstName={FirstName}, LastName={LastName}, Department={Department}, Email={Email}, Groups={GroupCount}", 
                    userInfo.DisplayName, userInfo.FirstName, userInfo.LastName, userInfo.Department, userInfo.Email, userInfo.Groups.Length);

                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get user information");
                return new UserInfo
                {
                    Username = Environment.UserName,
                    ComputerName = Environment.MachineName,
                    Domain = Environment.UserDomainName
                };
            }
        }

        private string GetUserFullName()
        {
            try
            {
                // Try to get from Active Directory first
                var adInfo = GetActiveDirectoryUserInfo();
                if (!string.IsNullOrEmpty(adInfo.DisplayName))
                {
                    return adInfo.DisplayName;
                }

                // Fallback to Windows identity
                using var identity = WindowsIdentity.GetCurrent();
                if (identity != null)
                {
                    // Try to get display name from UserPrincipal
                    try
                    {
                        using var context = new PrincipalContext(ContextType.Domain);
                        using var user = UserPrincipal.FindByIdentity(context, Environment.UserName);
                        if (user != null && !string.IsNullOrEmpty(user.DisplayName))
                        {
                            return user.DisplayName;
                        }
                    }
                    catch
                    {
                        // Fallback to local context
                        try
                        {
                            using var context = new PrincipalContext(ContextType.Machine);
                            using var user = UserPrincipal.FindByIdentity(context, Environment.UserName);
                            if (user != null && !string.IsNullOrEmpty(user.DisplayName))
                            {
                                return user.DisplayName;
                            }
                        }
                        catch
                        {
                            // Ignore errors, return username as fallback
                        }
                    }
                }

                return Environment.UserName;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to get user full name");
                return Environment.UserName;
            }
        }

        private string GetUserDepartment()
        {
            try
            {
                // Try to get from Active Directory first
                var adInfo = GetActiveDirectoryUserInfo();
                if (!string.IsNullOrEmpty(adInfo.Department))
                {
                    return adInfo.Department;
                }

                // Fallback to group membership analysis
                var groups = GetUserGroups();
                
                // Look for department-related groups
                var departmentGroups = groups.Where(g => 
                    g.Contains("IT", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Sales", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Marketing", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("HR", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Finance", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Engineering", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Support", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Admin", StringComparison.OrdinalIgnoreCase)
                ).ToArray();

                if (departmentGroups.Length > 0)
                {
                    // Return the first department group found
                    var department = departmentGroups[0];
                    if (department.Contains('\\'))
                    {
                        department = department.Split('\\').Last();
                    }
                    return department;
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to get user department");
                return "Unknown";
            }
        }

        private (string DisplayName, string Department, string FirstName, string LastName, string Email) GetActiveDirectoryUserInfo()
        {
            try
            {
                _logger.Information("Attempting to query Active Directory for user: {Username}", Environment.UserName);
                
                using var context = new PrincipalContext(ContextType.Domain);
                _logger.Information("PrincipalContext created successfully for domain");
                
                using var user = UserPrincipal.FindByIdentity(context, Environment.UserName);
                
                if (user != null)
                {
                    _logger.Information("UserPrincipal found successfully for user: {Username}", Environment.UserName);
                    
                    var displayName = user.DisplayName ?? string.Empty;
                    var firstName = user.GivenName ?? string.Empty;
                    var lastName = user.Surname ?? string.Empty;
                    var email = user.EmailAddress ?? string.Empty;

                    _logger.Information("Basic AD attributes retrieved - DisplayName: {DisplayName}, FirstName: {FirstName}, LastName: {LastName}, Email: {Email}", 
                        displayName, firstName, lastName, email);

                    // Get department from DirectoryEntry
                    string department = string.Empty;
                    try
                    {
                        var directoryEntry = user.GetUnderlyingObject() as DirectoryEntry;
                        if (directoryEntry != null)
                        {
                            _logger.Information("DirectoryEntry retrieved successfully, attempting to get department attribute");
                            
                            if (directoryEntry.Properties.Contains("department"))
                            {
                                department = directoryEntry.Properties["department"].Value?.ToString() ?? string.Empty;
                                _logger.Information("Department attribute found: {Department}", department);
                            }
                            else
                            {
                                _logger.Warning("Department attribute not found in DirectoryEntry properties");
                            }
                        }
                        else
                        {
                            _logger.Warning("Failed to get DirectoryEntry from UserPrincipal");
                        }
                    }
                    catch (Exception deEx)
                    {
                        _logger.Warning(deEx, "Error accessing DirectoryEntry for department attribute");
                    }

                    var result = (displayName, department, firstName, lastName, email);
                    _logger.Information("AD Info retrieved: DisplayName={DisplayName}, Department={Department}, FirstName={FirstName}, LastName={LastName}, Email={Email}", 
                        result.displayName, result.department, result.firstName, result.lastName, result.email);
                    return result;
                }
                else
                {
                    _logger.Warning("UserPrincipal.FindByIdentity returned null for user: {Username}", Environment.UserName);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get Active Directory user info for user: {Username}", Environment.UserName);
            }

            _logger.Information("Returning empty AD info due to failure or null user");
            return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        private string[] GetUserGroups()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity?.Groups == null)
                {
                    _logger.Warning("Could not retrieve user's security groups from Windows identity.");
                    return Array.Empty<string>();
                }

                var groups = new List<string>();
                foreach (var group in identity.Groups)
                {
                    try
                    {
                        var groupName = group.Translate(typeof(NTAccount)).Value;
                        groups.Add(groupName);
                    }
                    catch (IdentityNotMappedException)
                    {
                        // Fallback to SID if name can't be resolved
                        groups.Add(group.Value);
                    }
                }

                return groups.ToArray();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to get user groups");
                return Array.Empty<string>();
            }
        }

        public bool IsUserInGroup(string groupName)
        {
            try
            {
                var groups = GetUserGroups();
                return groups.Any(g => g.Equals(groupName, StringComparison.OrdinalIgnoreCase) || 
                                     g.EndsWith("\\" + groupName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to check if user is in group: {GroupName}", groupName);
                return false;
            }
        }

        public bool IsUserInAnyGroup(string[] groupNames)
        {
            if (groupNames == null || groupNames.Length == 0)
                return true;

            foreach (var groupName in groupNames)
            {
                if (IsUserInGroup(groupName))
                {
                    _logger.Information("User is a member of '{Group}'", groupName);
                    return true;
                }
            }

            return false;
        }
    }
} 