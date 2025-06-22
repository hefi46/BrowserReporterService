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
        public string ServerUrl { get; set; } = string.Empty;

        [JsonProperty("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonProperty("sync_interval_minutes")]
        public int SyncIntervalMinutes { get; set; } = 5;

        [JsonProperty("retry_interval_seconds")]
        public int RetryIntervalSeconds { get; set; } = 300;

        [JsonProperty("max_history_age_hours")]
        public int MaxHistoryAgeHours { get; set; } = 720;

        [JsonProperty("monitored_users_group")]
        public string MonitoredUsersGroup { get; set; } = string.Empty;

        [JsonProperty("monitored_users")]
        public string[] MonitoredUsers { get; set; } = Array.Empty<string>();

        [JsonProperty("monitored_hours")]
        public MonitoredHoursConfig MonitoredHours { get; set; } = new();

        [JsonProperty("browsers")]
        public string[] Browsers { get; set; } = new[] { "chrome", "edge" };

        [JsonProperty("ldap")]
        public LdapConfig Ldap { get; set; } = new();

        [JsonProperty("enable_group_filtering")]
        public bool EnableGroupFiltering { get; set; } = true;

        [JsonProperty("security_groups")]
        public string[] SecurityGroups { get; set; } = Array.Empty<string>();

        [JsonProperty("prefer_local_group_lookup")]
        public bool PreferLocalGroupLookup { get; set; } = true;

        [JsonProperty("log_max_mb")]
        public int LogMaxMb { get; set; } = 5;

        [JsonProperty("log_roll_count")]
        public int LogRollCount { get; set; } = 3;
    }

    public class MonitoredHoursConfig
    {
        [JsonProperty("start")]
        public string Start { get; set; } = "00:00";

        [JsonProperty("end")]
        public string End { get; set; } = "23:59";
    }

    public class LdapConfig
    {
        [JsonProperty("server")]
        public string Server { get; set; } = string.Empty;

        [JsonProperty("user_search_base")]
        public string UserSearchBase { get; set; } = string.Empty;
    }
} 