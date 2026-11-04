using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenyComes.Models;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

public class OsuLibraryCacheDao
{
    private static readonly Lazy<OsuLibraryCacheDao> _lazy = new(() => new OsuLibraryCacheDao());
    public static OsuLibraryCacheDao Instance => _lazy.Value;

    private readonly DatabaseContext _db = DatabaseContext.Instance;
    private bool _tablesInitialized = false;
    private readonly object _initLock = new();

    private OsuLibraryCacheDao() { }

    public void EnsureTablesCreated()
    {
        if (_tablesInitialized) return;
        lock (_initLock)
        {
            if (_tablesInitialized) return;
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS osu_tracks (
                    id INTEGER PRIMARY KEY,
                    file_path TEXT NOT NULL,
                    title TEXT NOT NULL,
                    artist TEXT NOT NULL,
                    album TEXT,
                    album_artist TEXT,
                    genre TEXT,
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    file_format TEXT,
                    artwork_path TEXT,
                    video_path TEXT,
                    video_offset_ms INTEGER NOT NULL DEFAULT 0,
                    osu_source TEXT,
                    osu_artist_roumaji TEXT,
                    osu_title_roumaji TEXT,
                    osu_artist_unicode TEXT,
                    osu_title_unicode TEXT,
                    date_added TEXT NOT NULL,
                    first_letter_group TEXT
                );

                CREATE TABLE IF NOT EXISTS osu_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT
                );
            ";
            cmd.ExecuteNonQuery();
            _tablesInitialized = true;
        }
    }

    public async Task EnsureTablesCreatedAsync()
    {
        if (_tablesInitialized) return;
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS osu_tracks (
                id INTEGER PRIMARY KEY,
                file_path TEXT NOT NULL,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                album TEXT,
                album_artist TEXT,
                genre TEXT,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                file_format TEXT,
                artwork_path TEXT,
                video_path TEXT,
                video_offset_ms INTEGER NOT NULL DEFAULT 0,
                osu_source TEXT,
                osu_artist_roumaji TEXT,
                osu_title_roumaji TEXT,
                osu_artist_unicode TEXT,
                osu_title_unicode TEXT,
                date_added TEXT NOT NULL,
                first_letter_group TEXT
            );

            CREATE TABLE IF NOT EXISTS osu_meta (
                key TEXT PRIMARY KEY,
                value TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();
        _tablesInitialized = true;
    }

    public string? GetDbVersion()
    {
        EnsureTablesCreated();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM osu_meta WHERE key = 'db_version';";
        var res = cmd.ExecuteScalar();
        return res?.ToString();
    }

    public async Task<string?> GetDbVersionAsync()
    {
        await EnsureTablesCreatedAsync();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM osu_meta WHERE key = 'db_version';";
        var res = await cmd.ExecuteScalarAsync();
        return res?.ToString();
    }

    public void SaveDbVersion(string version)
    {
        EnsureTablesCreated();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO osu_meta (key, value) VALUES ('db_version', $val)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";
        cmd.Parameters.AddWithValue("$val", version ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public async Task SaveDbVersionAsync(string version)
    {
        await EnsureTablesCreatedAsync();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO osu_meta (key, value) VALUES ('db_version', $val)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";
        cmd.Parameters.AddWithValue("$val", version ?? string.Empty);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveTracksAsync(List<Track> tracks)
    {
        if (tracks == null || tracks.Count == 0) return;

        await EnsureTablesCreatedAsync();
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var clearCmd = conn.CreateCommand())
            {
                clearCmd.Transaction = tx;
                clearCmd.CommandText = "DELETE FROM osu_tracks;";
                await clearCmd.ExecuteNonQueryAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO osu_tracks (
                    id, file_path, title, artist, album, album_artist, genre,
                    duration_ms, file_format, artwork_path, video_path, video_offset_ms,
                    osu_source, osu_artist_roumaji, osu_title_roumaji, osu_artist_unicode, osu_title_unicode,
                    date_added, first_letter_group
                ) VALUES (
                    $id, $filePath, $title, $artist, $album, $albumArtist, $genre,
                    $durationMs, $fileFormat, $artworkPath, $videoPath, $videoOffsetMs,
                    $osuSource, $osuArtistRoumaji, $osuTitleRoumaji, $osuArtistUnicode, $osuTitleUnicode,
                    $dateAdded, $firstLetterGroup
                );
            ";

            var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
            var pFilePath = cmd.Parameters.Add("$filePath", SqliteType.Text);
            var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
            var pArtist = cmd.Parameters.Add("$artist", SqliteType.Text);
            var pAlbum = cmd.Parameters.Add("$album", SqliteType.Text);
            var pAlbumArtist = cmd.Parameters.Add("$albumArtist", SqliteType.Text);
            var pGenre = cmd.Parameters.Add("$genre", SqliteType.Text);
            var pDurationMs = cmd.Parameters.Add("$durationMs", SqliteType.Integer);
            var pFileFormat = cmd.Parameters.Add("$fileFormat", SqliteType.Text);
            var pArtworkPath = cmd.Parameters.Add("$artworkPath", SqliteType.Text);
            var pVideoPath = cmd.Parameters.Add("$videoPath", SqliteType.Text);
            var pVideoOffsetMs = cmd.Parameters.Add("$videoOffsetMs", SqliteType.Integer);
            var pOsuSource = cmd.Parameters.Add("$osuSource", SqliteType.Text);
            var pOsuArtistRoumaji = cmd.Parameters.Add("$osuArtistRoumaji", SqliteType.Text);
            var pOsuTitleRoumaji = cmd.Parameters.Add("$osuTitleRoumaji", SqliteType.Text);
            var pOsuArtistUnicode = cmd.Parameters.Add("$osuArtistUnicode", SqliteType.Text);
            var pOsuTitleUnicode = cmd.Parameters.Add("$osuTitleUnicode", SqliteType.Text);
            var pDateAdded = cmd.Parameters.Add("$dateAdded", SqliteType.Text);
            var pFirstLetterGroup = cmd.Parameters.Add("$firstLetterGroup", SqliteType.Text);

            foreach (var t in tracks)
            {
                pId.Value = t.Id;
                pFilePath.Value = t.FilePath ?? string.Empty;
                pTitle.Value = t.Title ?? string.Empty;
                pArtist.Value = t.Artist ?? string.Empty;
                pAlbum.Value = t.Album ?? string.Empty;
                pAlbumArtist.Value = t.AlbumArtist ?? string.Empty;
                pGenre.Value = t.Genre ?? string.Empty;
                pDurationMs.Value = (long)t.Duration.TotalMilliseconds;
                pFileFormat.Value = t.FileFormat ?? string.Empty;
                pArtworkPath.Value = t.ArtworkPath ?? string.Empty;
                pVideoPath.Value = t.VideoPath ?? string.Empty;
                pVideoOffsetMs.Value = t.VideoOffsetMs;
                pOsuSource.Value = t.OsuSource ?? string.Empty;
                pOsuArtistRoumaji.Value = t.OsuArtistRoumaji ?? string.Empty;
                pOsuTitleRoumaji.Value = t.OsuTitleRoumaji ?? string.Empty;
                pOsuArtistUnicode.Value = t.OsuArtistUnicode ?? string.Empty;
                pOsuTitleUnicode.Value = t.OsuTitleUnicode ?? string.Empty;
                pDateAdded.Value = t.DateAdded.ToString("O");
                pFirstLetterGroup.Value = t.FirstLetterGroup ?? string.Empty;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            System.Diagnostics.Debug.WriteLine($"[OsuLibraryCacheDao] Error saving tracks: {ex.Message}");
            throw;
        }
    }

    public async Task<List<Track>?> TryLoadTracksAsync()
    {
        await EnsureTablesCreatedAsync();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                id, file_path, title, artist, album, album_artist, genre,
                duration_ms, file_format, artwork_path, video_path, video_offset_ms,
                osu_source, osu_artist_roumaji, osu_title_roumaji, osu_artist_unicode, osu_title_unicode,
                date_added, first_letter_group
            FROM osu_tracks
            ORDER BY title COLLATE NOCASE ASC;
        ";

        var list = new List<Track>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var track = new Track
            {
                Id = reader.GetInt64(0),
                FilePath = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.GetString(3),
                Album = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                AlbumArtist = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Genre = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Duration = TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                FileFormat = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                ArtworkPath = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                VideoPath = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                VideoOffsetMs = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                OsuSource = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                OsuArtistRoumaji = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                OsuTitleRoumaji = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                OsuArtistUnicode = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                OsuTitleUnicode = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                DateAdded = DateTime.TryParse(reader.GetString(17), out var da) ? da : DateTime.UtcNow,
                FirstLetterGroup = reader.IsDBNull(18) ? string.Empty : reader.GetString(18)
            };
            list.Add(track);
        }

        return list.Count > 0 ? list : null;
    }

    public async Task ClearCacheAsync()
    {
        await EnsureTablesCreatedAsync();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM osu_tracks;
            DELETE FROM osu_meta;
        ";
        await cmd.ExecuteNonQueryAsync();
    }
}
