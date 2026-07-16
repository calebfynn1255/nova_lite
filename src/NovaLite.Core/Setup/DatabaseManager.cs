using SQLite;
using NovaLite.Core.Settings;

namespace NovaLite.Setup;

public static class DatabaseManager
{
    private static SQLiteAsyncConnection? _connection;

    public static async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
            return _connection;

        var dir = AppSettings.Load().ModelDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NovaLiteModels");
            Directory.CreateDirectory(dir);
            var settings = AppSettings.Load();
            settings.ModelDirectory = dir;
            settings.Save();
        }

        var dbPath = Path.Combine(dir, "novalite.db");
        _connection = new SQLiteAsyncConnection(dbPath);

        // Initialize tables
        await _connection.CreateTableAsync<HardwareProfile>();
        await _connection.CreateTableAsync<BenchmarkResult>();

        return _connection;
    }
}
