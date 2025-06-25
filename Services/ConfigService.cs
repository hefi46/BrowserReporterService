namespace BrowserReporterService.Services
{
    public class ConfigService
    {
        private readonly CryptoService _cryptoService;
        private readonly HttpClient _httpClient;
        private readonly Serilog.ILogger _logger;

        public ConfigService(Serilog.ILogger logger)
        {
            _cryptoService = new CryptoService();
            _httpClient = new HttpClient();
            _logger = logger;
        }

        public async Task<AppConfig?> GetConfigAsync(string? localConfigPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(localConfigPath))
                {
                    _logger.Information("Loading configuration from local path: {Path}", localConfigPath);
                    var plaintextJson = await File.ReadAllTextAsync(localConfigPath);
                    // A local config is not encrypted.
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<AppConfig>(plaintextJson);
                }

                // Try to get bootstrap config first to determine server URL and API key
                var bootstrapConfig = await GetBootstrapConfigAsync();
                if (bootstrapConfig == null)
                {
                    _logger.Error("Failed to load bootstrap configuration. Cannot download secure config.");
                    return null;
                }

                var secureConfigUrl = $"{bootstrapConfig.ServerUrl.TrimEnd('/')}/secureconfig.json";
                _logger.Information("Downloading secure configuration from: {Url}", secureConfigUrl);
                
                var request = new HttpRequestMessage(HttpMethod.Get, secureConfigUrl);
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var secureJson = await response.Content.ReadAsStringAsync();
                
                _logger.Information("Configuration downloaded, attempting to decrypt and verify.");
                var config = _cryptoService.DecryptConfig(secureJson);
                if (config != null)
                {
                    if (IsConfigValid(config))
                    {
                        _logger.Information("Configuration successfully decrypted and verified.");
                        return config;
                    }
                    _logger.Error("Configuration validation failed. Essential fields like 'server_url' may be missing or empty.");
                    return null;
                }
                _logger.Warning("Failed to deserialize decrypted configuration.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get application configuration.");
                return null;
            }
        }

        private bool IsConfigValid(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                _logger.Error("Configuration is invalid: 'server_url' is missing.");
                return false;
            }

            _logger.Information("Configuration validation passed. Server URL: {ServerUrl}", config.ServerUrl);
            return true;
        }

        public void EncryptAndOutputConfig(string configPath)
        {
            try
            {
                _logger.Information("Reading plaintext config from {Path} to encrypt.", configPath);
                var plaintextJson = File.ReadAllText(configPath);
                var encryptedEnvelope = _cryptoService.EncryptConfig(plaintextJson);
                Console.WriteLine(encryptedEnvelope);
                _logger.Information("Encrypted config printed to stdout.");
            }
            catch(Exception ex)
            {
                _logger.Error(ex, "Failed to encrypt configuration file.");
                Console.WriteLine($"Error: Could not read or encrypt the file at {configPath}.");
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Gets a minimal bootstrap configuration containing server URL and API key.
        /// Uses hardcoded DNS name 'browserreporter' for production deployments.
        /// </summary>
        private async Task<AppConfig?> GetBootstrapConfigAsync()
        {
            try
            {
                // Try bootstrap config file
                var bootstrapPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "BrowserReporter", "bootstrap.json");
                    
                if (File.Exists(bootstrapPath))
                {
                    _logger.Information("Loading bootstrap config from: {Path}", bootstrapPath);
                    var json = await File.ReadAllTextAsync(bootstrapPath);
                    var bootstrap = Newtonsoft.Json.JsonConvert.DeserializeObject<AppConfig>(json);
                    if (bootstrap != null && !string.IsNullOrEmpty(bootstrap.ServerUrl))
                    {
                        return bootstrap;
                    }
                }
                
                // Use hardcoded DNS name for production deployment
                _logger.Information("No bootstrap config found. Using hardcoded DNS name 'browserreporter'.");
                return new AppConfig 
                { 
                    ServerUrl = "http://browserreporter:8000"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load bootstrap configuration");
                return null;
            }
        }
    }
} 