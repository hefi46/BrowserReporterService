using Newtonsoft.Json;

namespace BrowserReporterService.Services
{
    // For the downloaded secure envelope
    public class SecureConfigEnvelope
    {
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("encrypted_data")]
        public string EncryptedData { get; set; } = string.Empty;

        [JsonProperty("iv")]
        public string Iv { get; set; } = string.Empty;

        [JsonProperty("checksum")]
        public string Checksum { get; set; } = string.Empty;

        [JsonProperty("signature")]
        public string Signature { get; set; } = string.Empty;
    }

    // For the decrypted plaintext configuration
    public class AppConfig
    {
        [JsonProperty("server_url")]
        public string ServerUrl { get; set; } = "";

        [JsonProperty("sync_interval_minutes")]
        public int SyncIntervalMinutes { get; set; } = 5;

        [JsonProperty("max_history_age_hours")]
        public int MaxHistoryAgeHours { get; set; } = 24;

        [JsonProperty("monitored_users_group")]
        public string MonitoredUsersGroup { get; set; } = "";

        [JsonProperty("monitored_users")]
        public List<string> MonitoredUsers { get; set; } = new();

        [JsonProperty("monitored_hours")]
        public MonitoredHoursConfig MonitoredHours { get; set; } = new();

        [JsonProperty("browsers")]
        public List<string> Browsers { get; set; } = new() { "chrome", "edge" };

        [JsonProperty("log_max_mb")]
        public int LogMaxMb { get; set; } = 5;

        [JsonProperty("log_roll_count")]
        public int LogRollCount { get; set; } = 3;

        [JsonProperty("exit_password")]
        public string ExitPassword { get; set; } = "BRAdmin2025";
    }

    public class MonitoredHoursConfig
    {
        [JsonProperty("start")]
        public string Start { get; set; } = "00:00";

        [JsonProperty("end")]
        public string End { get; set; } = "23:59";
    }
} 