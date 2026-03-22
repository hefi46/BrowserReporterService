using BrowserReporterService.Services;
using Microsoft.Data.Sqlite;
using Serilog;
using SQLitePCL;

namespace BrowserReporterService.Tests;

public class CacheServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Serilog.ILogger _logger;

    public CacheServiceTests()
    {
        Batteries.Init();
        _logger = new LoggerConfiguration().CreateLogger();
        // CacheService uses a fixed path — we test via a real instance
        // Each test gets a fresh database by clearing it
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dbPath = Path.Combine(appDataPath, "BrowserReporter", "sent_cache_test.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        // Clean up any previous test DB
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private SqliteConnection CreateTestConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS sent_items(id TEXT PRIMARY KEY, sent_at TEXT);";
        cmd.ExecuteNonQuery();
        return conn;
    }

    [Fact]
    public void CacheService_InitializesDatabase()
    {
        using var cache = new CacheService(_logger);
        var keys = cache.GetSentItemKeys();
        Assert.NotNull(keys);
    }

    [Fact]
    public void AddAndRetrieve_ItemsRoundTrip()
    {
        using var cache = new CacheService(_logger);

        var items = new[] { "http://example.com:1000", "http://test.com:2000" };
        cache.AddSentItems(items);

        var keys = cache.GetSentItemKeys();
        Assert.Contains("http://example.com:1000", keys);
        Assert.Contains("http://test.com:2000", keys);
    }

    [Fact]
    public void AddSentItems_DuplicatesIgnored()
    {
        using var cache = new CacheService(_logger);

        var items = new[] { "http://dup.com:1000" };
        cache.AddSentItems(items);
        cache.AddSentItems(items); // Insert same item again

        var keys = cache.GetSentItemKeys();
        Assert.Single(keys.Where(k => k == "http://dup.com:1000"));
    }

    [Fact]
    public void AddSentItems_EmptyList_NoOp()
    {
        using var cache = new CacheService(_logger);
        cache.AddSentItems(Enumerable.Empty<string>()); // Should not throw
    }

    [Fact]
    public void PruneOldEntries_RemovesExpired()
    {
        using var cache = new CacheService(_logger);

        // Add an item, then manually backdate it in the DB
        cache.AddSentItems(new[] { "old-item:1000" });
        cache.AddSentItems(new[] { "new-item:2000" });

        // Backdate old-item to 30 days ago via direct SQL
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appDataPath, "BrowserReporter", "sent_cache.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sent_items SET sent_at = $old WHERE id = 'old-item:1000';";
            cmd.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-30).ToString("o"));
            cmd.ExecuteNonQuery();
        }

        var pruned = cache.PruneOldEntries(7);
        Assert.True(pruned >= 1, $"Expected at least 1 pruned entry, got {pruned}");

        var remaining = cache.GetSentItemKeys();
        Assert.DoesNotContain("old-item:1000", remaining);
        Assert.Contains("new-item:2000", remaining);
    }

    [Fact]
    public void PruneOldEntries_NothingOld_ReturnsZero()
    {
        using var cache = new CacheService(_logger);
        cache.AddSentItems(new[] { "fresh:1000" });

        var pruned = cache.PruneOldEntries(7);
        Assert.Equal(0, pruned);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
