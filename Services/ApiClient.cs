using Newtonsoft.Json;
using System.Text;
using System.Net.Http.Headers;

namespace BrowserReporterService.Services
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly Serilog.ILogger _logger;
        private readonly AppConfig _config;

        public ApiClient(Serilog.ILogger logger, AppConfig config)
        {
            _logger = logger;
            _config = config;

            var handler = new HttpClientHandler
            {
                // Allow self-signed certificates for testing/staging environments
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
        }

        public async Task<bool> SendReportAsync(ReportPayload payload)
        {
            if (payload.Visits.Count == 0)
            {
                _logger.Information("No new visits to report. Skipping API call.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
            {
                _logger.Error("API call failed: Server URL is not configured.");
                return false;
            }

            var endpoint = new Uri(new Uri(_config.ServerUrl), "/api/ingest/browsing");
            
            // Convert to the format expected by the server
            var browsingPayload = new BrowsingIngestPayload
            {
                Username = payload.Username,
                Visits = payload.Visits
            };
            
            var jsonPayload = JsonConvert.SerializeObject(browsingPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            _logger.Information("Sending {Count} visits to {Endpoint}", payload.Visits.Count, endpoint);

            try
            {
                var response = await _httpClient.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("Successfully sent report to the server. Status code: {StatusCode}", response.StatusCode);
                    return true;
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.Error("Failed to send report. Server responded with {StatusCode}. Response: {Response}", response.StatusCode, responseContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An exception occurred while sending the report to the server.");
                return false;
            }
        }

        public async Task<bool> UploadScreenshotAsync(byte[] imageBytes, string username)
        {
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
            {
                _logger.Error("Screenshot upload failed: Server URL is not configured.");
                return false;
            }

            var endpoint = new Uri(new Uri(_config.ServerUrl), "/api/screenshot");
            var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "file", "screenshot.png");
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(Environment.MachineName), "computerName");

            _logger.Information("Uploading screenshot for user {User} to {Endpoint}", username, endpoint);

            try
            {
                var response = await _httpClient.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("Screenshot uploaded successfully. Status code: {StatusCode}", response.StatusCode);
                    return true;
                }
                else
                {
                    var respContent = await response.Content.ReadAsStringAsync();
                    _logger.Error("Screenshot upload failed. Status code: {StatusCode}. Response: {Response}", response.StatusCode, respContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An exception occurred while uploading the screenshot.");
                return false;
            }
        }

        public async Task<bool?> SendHeartbeatAsync(HeartbeatPayload payload)
        {
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
            {
                _logger.Error("Heartbeat failed: Server URL is not configured.");
                return null;
            }

            var endpoint = new Uri(new Uri(_config.ServerUrl), "/api/ingest/heartbeat");
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var respJson = await response.Content.ReadAsStringAsync();
                    var hbResp = JsonConvert.DeserializeObject<HeartbeatResponse>(respJson);
                    _logger.Information("Heartbeat sent successfully. Screenshot requested: {Request}", hbResp?.RequestScreenshot ?? false);
                    return hbResp?.RequestScreenshot;
                }
                else
                {
                    _logger.Warning("Heartbeat failed. Status: {Status}", response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception occurred while sending heartbeat.");
                return null;
            }
        }
    }
} 