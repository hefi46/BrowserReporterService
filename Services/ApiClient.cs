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

            var endpoint = new Uri(new Uri(_config.ServerUrl), "/api/reports/data");
            
            var jsonPayload = JsonConvert.SerializeObject(payload);
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
    }
} 