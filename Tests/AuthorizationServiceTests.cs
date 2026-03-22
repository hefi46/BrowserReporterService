using BrowserReporterService.Services;
using Serilog;

namespace BrowserReporterService.Tests;

public class AuthorizationServiceTests
{
    private readonly AuthorizationService _authService;

    public AuthorizationServiceTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _authService = new AuthorizationService(logger);
    }

    // --- IsMonitoringTimeActive ---

    [Fact]
    public void IsMonitoringTimeActive_DefaultConfig_ReturnsTrue()
    {
        // Default is 00:00 to 23:59 — always active
        var config = new AppConfig();
        Assert.True(_authService.IsMonitoringTimeActive(config));
    }

    [Fact]
    public void IsMonitoringTimeActive_InvalidFormat_ReturnsFalse()
    {
        var config = new AppConfig
        {
            MonitoredHours = new MonitoredHoursConfig { Start = "invalid", End = "also-invalid" }
        };
        // After our fix, invalid format should fail closed
        Assert.False(_authService.IsMonitoringTimeActive(config));
    }

    [Fact]
    public void IsMonitoringTimeActive_InvalidStart_ReturnsFalse()
    {
        var config = new AppConfig
        {
            MonitoredHours = new MonitoredHoursConfig { Start = "bad", End = "17:00" }
        };
        Assert.False(_authService.IsMonitoringTimeActive(config));
    }

    [Fact]
    public void IsMonitoringTimeActive_NormalWindow_CurrentInside()
    {
        // Set window to include current hour
        var now = DateTime.Now.TimeOfDay;
        var start = (now - TimeSpan.FromHours(1));
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;
        var end = (now + TimeSpan.FromHours(1));
        if (end > new TimeSpan(23, 59, 0)) end = new TimeSpan(23, 59, 0);

        var config = new AppConfig
        {
            MonitoredHours = new MonitoredHoursConfig
            {
                Start = start.ToString(@"hh\:mm"),
                End = end.ToString(@"hh\:mm")
            }
        };
        Assert.True(_authService.IsMonitoringTimeActive(config));
    }

    [Fact]
    public void IsMonitoringTimeActive_NormalWindow_CurrentOutside()
    {
        // Set a narrow window that definitely doesn't include now
        // Use a 1-minute window at a time far from now
        var now = DateTime.Now.TimeOfDay;
        TimeSpan start, end;
        if (now.Hours < 12)
        {
            start = new TimeSpan(22, 0, 0);
            end = new TimeSpan(22, 1, 0);
        }
        else
        {
            start = new TimeSpan(3, 0, 0);
            end = new TimeSpan(3, 1, 0);
        }

        var config = new AppConfig
        {
            MonitoredHours = new MonitoredHoursConfig
            {
                Start = start.ToString(@"hh\:mm"),
                End = end.ToString(@"hh\:mm")
            }
        };
        Assert.False(_authService.IsMonitoringTimeActive(config));
    }

    // --- ShouldMonitorBrowser ---

    [Fact]
    public void ShouldMonitorBrowser_NullBrowserList_MonitorsAll()
    {
        var config = new AppConfig { Browsers = null! };
        Assert.True(_authService.ShouldMonitorBrowser(config, "chrome"));
        Assert.True(_authService.ShouldMonitorBrowser(config, "edge"));
        Assert.True(_authService.ShouldMonitorBrowser(config, "firefox"));
    }

    [Fact]
    public void ShouldMonitorBrowser_EmptyBrowserList_MonitorsAll()
    {
        var config = new AppConfig { Browsers = new List<string>() };
        Assert.True(_authService.ShouldMonitorBrowser(config, "chrome"));
    }

    [Fact]
    public void ShouldMonitorBrowser_ChromeOnly_ExcludesEdge()
    {
        var config = new AppConfig { Browsers = new List<string> { "chrome" } };
        Assert.True(_authService.ShouldMonitorBrowser(config, "chrome"));
        Assert.False(_authService.ShouldMonitorBrowser(config, "edge"));
    }

    [Fact]
    public void ShouldMonitorBrowser_CaseInsensitive()
    {
        var config = new AppConfig { Browsers = new List<string> { "Chrome", "Edge" } };
        Assert.True(_authService.ShouldMonitorBrowser(config, "chrome"));
        Assert.True(_authService.ShouldMonitorBrowser(config, "EDGE"));
    }

    // --- ShouldMonitorCurrentUser ---

    [Fact]
    public void ShouldMonitorCurrentUser_InMonitoredUsersList_ReturnsTrue()
    {
        var config = new AppConfig
        {
            MonitoredUsers = new List<string> { Environment.UserName }
        };
        Assert.True(_authService.ShouldMonitorCurrentUser(config));
    }

    [Fact]
    public void ShouldMonitorCurrentUser_NotInList_NoGroup_ReturnsFalse()
    {
        var config = new AppConfig
        {
            MonitoredUsers = new List<string> { "nonexistent-user-xyz" },
            MonitoredUsersGroup = ""
        };
        Assert.False(_authService.ShouldMonitorCurrentUser(config));
    }

    [Fact]
    public void ShouldMonitorCurrentUser_CaseInsensitiveMatch()
    {
        var config = new AppConfig
        {
            MonitoredUsers = new List<string> { Environment.UserName.ToUpper() }
        };
        Assert.True(_authService.ShouldMonitorCurrentUser(config));
    }
}
