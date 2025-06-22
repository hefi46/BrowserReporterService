using Newtonsoft.Json;

namespace BrowserReporterService.Services
{
    public class BrowserVisit
    {
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;
        
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonProperty("timestamp")]
        public long VisitTime { get; set; } // Unix Milliseconds
        
        [JsonProperty("duration_sec")]
        public int Duration { get; set; }
        
        [JsonProperty("browser")]
        public string Browser { get; set; } = string.Empty;
        
        public string ComputerName { get; set; } = string.Empty;
        public int VisitCount { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string BrowserType { get; set; } = string.Empty;

        // Composite key for the deduplication cache
        [JsonIgnore]
        public string CompositeKey => $"{Url}:{VisitTime}";
    }
    
    public class UserInfo
    {
        [JsonProperty("Username")]
        public string Username { get; set; } = string.Empty;
    }

    public class ReportPayload
    {
        [JsonProperty("Username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("Visits")]
        public List<BrowserVisit> Visits { get; set; } = new();

        [JsonProperty("UserInfo")]
        public UserInfo UserInfo { get; set; } = new();
    }

    public class HeartbeatPayload
    {
        [JsonProperty("username")] public string Username { get; set; } = string.Empty;
        [JsonProperty("ip")] public string IP { get; set; } = string.Empty;
        [JsonProperty("uptime_sec")] public long UptimeSeconds { get; set; }
    }

    public class HeartbeatResponse
    {
        [JsonProperty("requestScreenshot")] public bool RequestScreenshot { get; set; }
    }

    // New payload structure that matches the server's /api/ingest/browsing endpoint
    public class BrowsingIngestPayload
    {
        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("visits")]
        public List<BrowserVisit> Visits { get; set; } = new();
    }
} 