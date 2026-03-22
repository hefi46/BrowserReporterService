using BrowserReporterService.Services;
using Newtonsoft.Json;

namespace BrowserReporterService.Tests;

public class CryptoServiceTests
{
    private readonly CryptoService _crypto = new();

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var config = new AppConfig
        {
            ServerUrl = "http://test:8000",
            SyncIntervalMinutes = 10,
            MaxHistoryAgeHours = 48,
            Browsers = new List<string> { "chrome" }
        };

        var plaintext = JsonConvert.SerializeObject(config);
        var encrypted = _crypto.EncryptConfig(plaintext);
        var decrypted = _crypto.DecryptConfig(encrypted);

        Assert.NotNull(decrypted);
        Assert.Equal("http://test:8000", decrypted!.ServerUrl);
        Assert.Equal(10, decrypted.SyncIntervalMinutes);
        Assert.Equal(48, decrypted.MaxHistoryAgeHours);
        Assert.Contains("chrome", decrypted.Browsers);
    }

    [Fact]
    public void EncryptConfig_ProducesValidEnvelope()
    {
        var plaintext = JsonConvert.SerializeObject(new AppConfig { ServerUrl = "http://x" });
        var envelopeJson = _crypto.EncryptConfig(plaintext);
        var envelope = JsonConvert.DeserializeObject<SecureConfigEnvelope>(envelopeJson);

        Assert.NotNull(envelope);
        Assert.Equal("1.0", envelope!.Version);
        Assert.False(string.IsNullOrEmpty(envelope.EncryptedData));
        Assert.False(string.IsNullOrEmpty(envelope.Iv));
        Assert.False(string.IsNullOrEmpty(envelope.Checksum));
    }

    [Fact]
    public void DecryptConfig_TamperedData_ThrowsCryptographicException()
    {
        var plaintext = JsonConvert.SerializeObject(new AppConfig { ServerUrl = "http://x" });
        var envelopeJson = _crypto.EncryptConfig(plaintext);
        var envelope = JsonConvert.DeserializeObject<SecureConfigEnvelope>(envelopeJson)!;

        // Tamper with checksum
        envelope.Checksum = "0000000000000000000000000000000000000000000000000000000000000000";
        var tamperedJson = JsonConvert.SerializeObject(envelope);

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => _crypto.DecryptConfig(tamperedJson));
    }

    [Fact]
    public void DecryptConfig_NullInput_ReturnsNull()
    {
        var result = _crypto.DecryptConfig("null");
        Assert.Null(result);
    }

    [Fact]
    public void EncryptConfig_DifferentIVsEachTime()
    {
        var plaintext = JsonConvert.SerializeObject(new AppConfig { ServerUrl = "http://x" });
        var envelope1 = JsonConvert.DeserializeObject<SecureConfigEnvelope>(_crypto.EncryptConfig(plaintext))!;
        var envelope2 = JsonConvert.DeserializeObject<SecureConfigEnvelope>(_crypto.EncryptConfig(plaintext))!;

        // Same plaintext should produce different IVs and ciphertext
        Assert.NotEqual(envelope1.Iv, envelope2.Iv);
        Assert.NotEqual(envelope1.EncryptedData, envelope2.EncryptedData);
        // But same checksum (same plaintext)
        Assert.Equal(envelope1.Checksum, envelope2.Checksum);
    }

    [Fact]
    public void DecryptConfig_ServerExitPassword_IgnoredGracefully()
    {
        // The server still sends exit_password in config — verify it doesn't break deserialization
        var jsonWithExitPassword = @"{
            ""server_url"": ""http://test:8000"",
            ""sync_interval_minutes"": 5,
            ""exit_password"": ""BRAdmin2025""
        }";
        var encrypted = _crypto.EncryptConfig(jsonWithExitPassword);
        var decrypted = _crypto.DecryptConfig(encrypted);

        Assert.NotNull(decrypted);
        Assert.Equal("http://test:8000", decrypted!.ServerUrl);
        // exit_password is no longer a property — just verify no crash
    }
}
