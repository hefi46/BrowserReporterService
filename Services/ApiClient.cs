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
        private const int MaxRetries = 3;
        private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

        public ApiClient(Serilog.ILogger logger, AppConfig config)
        {
            _logger = logger;
            _config = config;

            var handler = new HttpClientHandler
            {
                // Allow self-signed certificates for testing/staging environments
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
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

            _logger.Information("Sending {Count} visits to {Endpoint}", payload.Visits.Count, endpoint);

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(endpoint, content);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.Information("Successfully sent report to the server. Status code: {StatusCode}", response.StatusCode);
                        return true;
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Don't retry client errors (4xx) — only server errors (5xx) are transient
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    {
                        _logger.Error("Failed to send report. Server responded with {StatusCode}. Response: {Response}", response.StatusCode, responseContent);
                        return false;
                    }

                    _logger.Warning("Server error {StatusCode} on attempt {Attempt}/{MaxRetries}. Response: {Response}",
                        response.StatusCode, attempt + 1, MaxRetries + 1, responseContent);
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    _logger.Warning("Request timed out on attempt {Attempt}/{MaxRetries}.", attempt + 1, MaxRetries + 1);
                }
                catch (HttpRequestException ex)
                {
                    _logger.Warning(ex, "Network error on attempt {Attempt}/{MaxRetries}.", attempt + 1, MaxRetries + 1);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "An unexpected exception occurred while sending the report to the server.");
                    return false;
                }

                if (attempt < MaxRetries)
                {
                    _logger.Information("Retrying in {Delay} seconds...", RetryDelays[attempt].TotalSeconds);
                    await Task.Delay(RetryDelays[attempt]);
                }
            }

            _logger.Error("Failed to send report after {MaxRetries} retries.", MaxRetries + 1);
            return false;
        }
    }
} 