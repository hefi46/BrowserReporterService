using BrowserReporterService.Services;
using Newtonsoft.Json;

namespace BrowserReporterService.Tests;

public class ModelTests
{
    // --- AppConfig defaults ---

    [Fact]
    public void AppConfig_Defaults()
    {
        var config = new AppConfig();
        Assert.Equal("", config.ServerUrl);
        Assert.Equal(5, config.SyncIntervalMinutes);
        Assert.Equal(24, config.MaxHistoryAgeHours);
        Assert.Equal("", config.MonitoredUsersGroup);
        Assert.Empty(config.MonitoredUsers);
        Assert.Equal(2, config.Browsers.Count);
        Assert.Contains("chrome", config.Browsers);
        Assert.Contains("edge", config.Browsers);
        Assert.Equal(5, config.LogMaxMb);
        Assert.Equal(3, config.LogRollCount);
    }

    [Fact]
    public void AppConfig_ExitPasswordRemoved()
    {
        // Verify exit_password property no longer exists
        var prop = typeof(AppConfig).GetProperty("ExitPassword");
        Assert.Null(prop);
    }

    [Fact]
    public void AppConfig_DeserializesFromServerJson()
    {
        // Simulate what the console server sends (including exit_password which we no longer use)
        var serverJson = @"{
            ""server_url"": ""http://prod:8000"",
            ""sync_interval_minutes"": 10,
            ""max_history_age_hours"": 48,
            ""monitored_users_group"": ""BrowserMonitoring"",
            ""monitored_users"": [""alice"", ""bob""],
            ""monitored_hours"": { ""start"": ""08:00"", ""end"": ""18:00"" },
            ""browsers"": [""chrome""],
            ""log_max_mb"": 10,
            ""log_roll_count"": 5,
            ""exit_password"": ""OldPassword123""
        }";

        var config = JsonConvert.DeserializeObject<AppConfig>(serverJson);

        Assert.NotNull(config);
        Assert.Equal("http://prod:8000", config!.ServerUrl);
        Assert.Equal(10, config.SyncIntervalMinutes);
        Assert.Equal(48, config.MaxHistoryAgeHours);
        Assert.Equal("BrowserMonitoring", config.MonitoredUsersGroup);
        Assert.Equal(2, config.MonitoredUsers.Count);
        Assert.Equal("08:00", config.MonitoredHours.Start);
        Assert.Equal("18:00", config.MonitoredHours.End);
        Assert.Contains("chrome", config.Browsers);
        Assert.Equal(10, config.LogMaxMb);
        Assert.Equal(5, config.LogRollCount);
    }

    [Fact]
    public void AppConfig_PartialJson_UsesDefaults()
    {
        var partialJson = @"{ ""server_url"": ""http://minimal:8000"" }";
        var config = JsonConvert.DeserializeObject<AppConfig>(partialJson);

        Assert.NotNull(config);
        Assert.Equal("http://minimal:8000", config!.ServerUrl);
        Assert.Equal(5, config.SyncIntervalMinutes); // default
        Assert.Equal(24, config.MaxHistoryAgeHours); // default
    }

    // --- MonitoredHoursConfig ---

    [Fact]
    public void MonitoredHoursConfig_Defaults()
    {
        var hours = new MonitoredHoursConfig();
        Assert.Equal("00:00", hours.Start);
        Assert.Equal("23:59", hours.End);
    }

    // --- BrowserVisit ---

    [Fact]
    public void BrowserVisit_CompositeKey_Format()
    {
        var visit = new BrowserVisit { Url = "http://example.com", VisitTime = 1700000000000 };
        Assert.Equal("http://example.com:1700000000000", visit.CompositeKey);
    }

    [Fact]
    public void BrowserVisit_CompositeKey_IsJsonIgnored()
    {
        var visit = new BrowserVisit
        {
            Url = "http://test.com",
            Title = "Test",
            VisitTime = 1234567890,
            ComputerName = "PC1"
        };
        var json = JsonConvert.SerializeObject(visit);
        Assert.DoesNotContain("CompositeKey", json);
    }

    [Fact]
    public void BrowserVisit_Serialization_MatchesServerContract()
    {
        var visit = new BrowserVisit
        {
            Url = "http://example.com",
            Title = "Example",
            VisitTime = 1700000000000,
            ComputerName = "WORKSTATION1"
        };
        var json = JsonConvert.SerializeObject(visit);
        var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;

        // These 4 fields are what the server consumes
        Assert.Equal("http://example.com", dict["Url"]);
        Assert.Equal("Example", dict["Title"]);
        Assert.Equal(1700000000000L, (long)dict["VisitTime"]);
        Assert.Equal("WORKSTATION1", dict["ComputerName"]);
    }

    // --- ReportPayload ---

    [Fact]
    public void ReportPayload_Serialization_MatchesServerContract()
    {
        var payload = new ReportPayload
        {
            Username = "testuser",
            Visits = new List<BrowserVisit>
            {
                new() { Url = "http://a.com", Title = "A", VisitTime = 1000, ComputerName = "PC" }
            },
            UserInfo = new UserInfo
            {
                Username = "testuser",
                DisplayName = "Test User",
                Department = "Engineering"
            }
        };

        var json = JsonConvert.SerializeObject(payload);
        var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;

        Assert.Equal("testuser", dict["Username"]);
        Assert.True(dict.ContainsKey("Visits"));
        Assert.True(dict.ContainsKey("UserInfo"));
    }

    // --- SecureConfigEnvelope ---

    [Fact]
    public void SecureConfigEnvelope_SnakeCaseJsonProperties()
    {
        var envelope = new SecureConfigEnvelope
        {
            Version = "1.0",
            EncryptedData = "abc",
            Iv = "def",
            Checksum = "ghi"
        };
        var json = JsonConvert.SerializeObject(envelope);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"encrypted_data\"", json);
        Assert.Contains("\"iv\"", json);
        Assert.Contains("\"checksum\"", json);
    }
}
