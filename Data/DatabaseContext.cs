using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

public class DatabaseContext
{
    private static readonly Lazy<DatabaseContext> _lazyInstance = new(() => new DatabaseContext());
    public static DatabaseContext Instance => _lazyInstance.Value;

    private readonly string _connectionString;

    private DatabaseContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "BenyComes");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "benycomes.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();

        command.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS tracks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                title TEXT,
                artist TEXT,
                album TEXT,
                year INTEGER,
                track_number INTEGER,
                genre TEXT,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                bitrate INTEGER NOT NULL DEFAULT 0,
                sample_rate INTEGER NOT NULL DEFAULT 44100,
                bits_per_sample INTEGER NOT NULL DEFAULT 16,
                channels INTEGER NOT NULL DEFAULT 2,
                format TEXT,
                date_added TEXT NOT NULL,
                date_modified TEXT NOT NULL DEFAULT '',
                play_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS library_folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                folder_path TEXT NOT NULL UNIQUE,
                added_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id INTEGER NOT NULL,
                track_id INTEGER NOT NULL,
                position INTEGER NOT NULL,
                PRIMARY KEY (playlist_id, position),
                FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS playback_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                track_id INTEGER NOT NULL,
                played_at TEXT NOT NULL,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS recent_plays (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                track_id INTEGER NOT NULL,
                album_name TEXT,
                played_at_timestamp INTEGER NOT NULL,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_recent_plays_ts ON recent_plays(played_at_timestamp DESC);

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cached_albums (
                name TEXT NOT NULL,
                artist TEXT NOT NULL,
                year INTEGER DEFAULT 0,
                genre TEXT DEFAULT '',
                track_count INTEGER DEFAULT 0,
                artwork_path TEXT DEFAULT '',
                PRIMARY KEY (name, artist)
            );

            CREATE TABLE IF NOT EXISTS cached_artists (
                name TEXT PRIMARY KEY,
                track_count INTEGER DEFAULT 0,
                album_count INTEGER DEFAULT 0,
                avatar_path TEXT DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS idx_tracks_file_path ON tracks(file_path);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
        ";

        command.ExecuteNonQuery();

        // Migrate columns if database already existed
        string[] migrations = new[]
        {
            "ALTER TABLE tracks ADD COLUMN date_modified TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE tracks ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE tracks ADD COLUMN album_artist TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE tracks ADD COLUMN comments TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE tracks ADD COLUMN file_size INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE playlists ADD COLUMN description TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE playlists ADD COLUMN custom_cover_path TEXT NOT NULL DEFAULT '';"
        };

        foreach (var sql in migrations)
        {
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = sql;
                alterCmd.ExecuteNonQuery();
            }
            catch
            {
                // Column already exists, ignore
            }
        }
    }
}
