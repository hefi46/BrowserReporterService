using Newtonsoft.Json;

namespace BrowserReporterService.Services
{
    public class BrowserVisit
    {
        [JsonProperty("Url")]
        public string Url { get; set; } = string.Empty;
        
        [JsonProperty("Title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonProperty("VisitTime")]
        public long VisitTime { get; set; } // Unix Milliseconds
        
        [JsonProperty("ComputerName")]
        public string ComputerName { get; set; } = string.Empty;
        
        public int Duration { get; set; }
        public string Browser { get; set; } = string.Empty;
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
        [JsonProperty("ComputerName")]
        public string ComputerName { get; set; } = string.Empty;
        [JsonProperty("Domain")]
        public string Domain { get; set; } = string.Empty;
        [JsonProperty("FullName")]
        public string FullName { get; set; } = string.Empty;
        [JsonProperty("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;
        [JsonProperty("FirstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonProperty("LastName")]
        public string LastName { get; set; } = string.Empty;
        [JsonProperty("Department")]
        public string Department { get; set; } = string.Empty;
        [JsonProperty("Email")]
        public string Email { get; set; } = string.Empty;
        [JsonProperty("Groups")]
        public string[] Groups { get; set; } = System.Array.Empty<string>();
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
} 