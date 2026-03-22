using Microsoft.Data.Sqlite;

namespace BrowserReporterService.Services
{
    public class CacheService : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Serilog.ILogger _logger;

        public CacheService(Serilog.ILogger logger)
        {
            _logger = logger;
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(appDataPath, "BrowserReporter", "sent_cache.db");
            
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var command = _connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS sent_items(id TEXT PRIMARY KEY, sent_at TEXT);";
            command.ExecuteNonQuery();
            _logger.Information("Deduplication cache database initialized.");
        }

        public HashSet<string> GetSentItemKeys()
        {
            var keys = new HashSet<string>();
            var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM sent_items;";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    keys.Add(reader.GetString(0));
                }
            }
            _logger.Information("Loaded {Count} keys from the deduplication cache.", keys.Count);
            return keys;
        }

        public void AddSentItems(IEnumerable<string> keys)
        {
            if (!keys.Any()) return;

            using (var transaction = _connection.BeginTransaction())
            {
                var command = _connection.CreateCommand();
                command.CommandText = "INSERT OR IGNORE INTO sent_items (id, sent_at) VALUES ($id, $sent_at);";
                command.Parameters.Add("$id", SqliteType.Text);
                command.Parameters.Add("$sent_at", SqliteType.Text);

                var now = DateTime.UtcNow.ToString("o");

                foreach (var key in keys)
                {
                    command.Parameters["$id"].Value = key;
                    command.Parameters["$sent_at"].Value = now;
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            _logger.Information("Added {Count} new keys to the deduplication cache.", keys.Count());
        }

        /// <summary>
        /// Removes cache entries older than the specified number of days to prevent unbounded growth.
        /// </summary>
        public int PruneOldEntries(int retentionDays = 7)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");
            var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM sent_items WHERE sent_at < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff);
            var deleted = command.ExecuteNonQuery();
            if (deleted > 0)
            {
                _logger.Information("Pruned {Count} expired entries from the deduplication cache (older than {Days} days).", deleted, retentionDays);
            }
            return deleted;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
} 