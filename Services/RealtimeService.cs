using Microsoft.AspNetCore.SignalR.Client;

namespace BrowserReporterService.Services
{
    public class RealtimeService : IAsyncDisposable
    {
        private readonly Serilog.ILogger _logger;
        private readonly AppConfig _config;
        private readonly ScreenshotService _screenshotService;
        private HubConnection? _connection;
        private readonly CancellationTokenSource _cts = new();

        public RealtimeService(Serilog.ILogger logger, AppConfig config)
        {
            _logger = logger;
            _config = config;
            _screenshotService = new ScreenshotService(_logger);
        }

        public async Task StartAsync()
        {
            try
            {
                var wsUrl = BuildWebSocketUrl(_config.ServerUrl);
                _logger.Information("Initializing SignalR connection to {Url}", wsUrl);

                _connection = new HubConnectionBuilder()
                    .WithUrl(wsUrl, opts =>
                    {
                        opts.Headers.Add("X-API-Key", _config.ApiKey);
                        opts.HttpMessageHandlerFactory = _ => new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                        };
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _connection.On<string>("RequestScreenshot", async (targetUser) =>
                {
                    try
                    {
                        if (!IsForThisUser(targetUser))
                        {
                            _logger.Debug("Screenshot request was for '{Target}' - ignoring.", targetUser);
                            return;
                        }
                        _logger.Information("Received RequestScreenshot command via SignalR.");
                        var imgBytes = _screenshotService.CaptureScreenshotPng();
                        var apiClient = new ApiClient(_logger, _config);
                        await apiClient.UploadScreenshotAsync(imgBytes, Environment.UserName);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed processing RequestScreenshot message.");
                    }
                });

                await _connection.StartAsync(_cts.Token);
                _logger.Information("SignalR connection established.");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to start SignalR realtime service. Remote screenshot requests will not be available.");
            }
        }

        private bool IsForThisUser(string targetUser)
        {
            return targetUser == "*" ||
                   string.Equals(targetUser, Environment.UserName, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildWebSocketUrl(string serverBaseUrl)
        {
            // Convert http/https to ws/wss and append /ws
            var uri = new Uri(serverBaseUrl);
            var scheme = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            var builder = new UriBuilder(uri) { Scheme = scheme, Port = uri.Port };
            var path = builder.Path.TrimEnd('/') + "/ws";
            builder.Path = path;
            return builder.Uri.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
        }
    }
} 