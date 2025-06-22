using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BrowserReporterService.Services
{
    public class WebSocketRealtimeService : IAsyncDisposable
    {
        private readonly Serilog.ILogger _logger;
        private readonly AppConfig _config;
        private readonly ScreenshotService _screenshotService;
        private ClientWebSocket? _webSocket;
        private readonly CancellationTokenSource _cts = new();
        private Task? _receiveTask;

        public WebSocketRealtimeService(Serilog.ILogger logger, AppConfig config)
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
                _logger.Information("Initializing WebSocket connection to {Url}", wsUrl);

                _webSocket = new ClientWebSocket();
                
                // Add API key header if available
                if (!string.IsNullOrEmpty(_config.ApiKey))
                {
                    _webSocket.Options.SetRequestHeader("X-API-Key", _config.ApiKey);
                }

                // Allow self-signed certificates for testing
                _webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);
                _logger.Information("WebSocket connection established.");

                // Start receiving messages
                _receiveTask = ReceiveLoop();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to start WebSocket realtime service. Remote screenshot requests will not be available.");
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            
            try
            {
                while (_webSocket?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.Information("WebSocket connection closed by server.");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Information("WebSocket receive loop cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in WebSocket receive loop.");
            }
        }

        private async Task HandleMessage(string message)
        {
            try
            {
                _logger.Debug("Received WebSocket message: {Message}", message);
                
                // Parse the JSON message from the server
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                // Check if this is a screenshot request
                if (root.TryGetProperty("type", out var typeElement) && 
                    typeElement.GetString() == "screenshot_request")
                {
                    var targetUser = "*"; // Default to all users
                    if (root.TryGetProperty("target_user", out var userElement))
                    {
                        targetUser = userElement.GetString() ?? "*";
                    }

                    await HandleScreenshotRequest(targetUser);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to handle WebSocket message: {Message}", message);
            }
        }

        private async Task HandleScreenshotRequest(string targetUser)
        {
            try
            {
                if (!IsForThisUser(targetUser))
                {
                    _logger.Debug("Screenshot request was for '{Target}' - ignoring.", targetUser);
                    return;
                }

                _logger.Information("Received screenshot request via WebSocket.");
                var imgBytes = _screenshotService.CaptureScreenshotPng();
                var apiClient = new ApiClient(_logger, _config);
                await apiClient.UploadScreenshotAsync(imgBytes, Environment.UserName);
                _logger.Information("Screenshot captured and uploaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed processing screenshot request.");
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
            
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client shutting down", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error closing WebSocket connection.");
                }
            }

            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error waiting for receive task completion.");
                }
            }

            _webSocket?.Dispose();
        }
    }
} 