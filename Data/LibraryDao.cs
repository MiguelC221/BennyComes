using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenyComes.Models;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

public class LibraryDao
{
    private static readonly Lazy<LibraryDao> _lazy = new(() => new LibraryDao());
    public static LibraryDao Instance => _lazy.Value;

    private readonly DatabaseContext _db = DatabaseContext.Instance;

    private LibraryDao() { }

    public async Task<long> InsertOrUpdateTrackAsync(Track track)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO tracks (
                file_path, title, artist, album, year, track_number, genre,
                duration_ms, bitrate, sample_rate, bits_per_sample, channels,
                format, date_added, date_modified
            ) VALUES (
                $filePath, $title, $artist, $album, $year, $trackNumber, $genre,
                $durationMs, $bitrate, $sampleRate, $bitsPerSample, $channels,
                $format, $dateAdded, $dateModified
            )
            ON CONFLICT(file_path) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                year = excluded.year,
                track_number = excluded.track_number,
                genre = excluded.genre,
                duration_ms = excluded.duration_ms,
                bitrate = excluded.bitrate,
                sample_rate = excluded.sample_rate,
                bits_per_sample = excluded.bits_per_sample,
                channels = excluded.channels,
                format = excluded.format,
                date_modified = excluded.date_modified
            RETURNING id;
        ";

        cmd.Parameters.AddWithValue("$filePath", track.FilePath);
        cmd.Parameters.AddWithValue("$title", track.Title);
        cmd.Parameters.AddWithValue("$artist", track.Artist);
        cmd.Parameters.AddWithValue("$album", track.Album);
        cmd.Parameters.AddWithValue("$year", (long)track.Year);
        cmd.Parameters.AddWithValue("$trackNumber", (long)track.TrackNumber);
        cmd.Parameters.AddWithValue("$genre", track.Genre ?? "");
        cmd.Parameters.AddWithValue("$durationMs", (long)track.Duration.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$bitrate", track.Bitrate);
        cmd.Parameters.AddWithValue("$sampleRate", track.SampleRate);
        cmd.Parameters.AddWithValue("$bitsPerSample", track.BitsPerSample);
        cmd.Parameters.AddWithValue("$channels", track.Channels);
        cmd.Parameters.AddWithValue("$format", track.FileFormat);
        cmd.Parameters.AddWithValue("$dateAdded", track.DateAdded.ToString("O"));
        cmd.Parameters.AddWithValue("$dateModified", track.DateModified.ToString("O"));

        var result = await cmd.ExecuteScalarAsync();
        if (result != null && long.TryParse(result.ToString(), out var id))
        {
            track.Id = id;
            return id;
        }

        return track.Id;
    }

    public async Task InsertOrUpdateTracksBatchAsync(IEnumerable<Track> tracks)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        cmd.CommandText = @"
            INSERT INTO tracks (
                file_path, title, artist, album, year, track_number, genre,
                duration_ms, bitrate, sample_rate, bits_per_sample, channels,
                format, date_added, date_modified
            ) VALUES (
                $filePath, $title, $artist, $album, $year, $trackNumber, $genre,
                $durationMs, $bitrate, $sampleRate, $bitsPerSample, $channels,
                $format, $dateAdded, $dateModified
            )
            ON CONFLICT(file_path) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                year = excluded.year,
                track_number = excluded.track_number,
                genre = excluded.genre,
                duration_ms = excluded.duration_ms,
                bitrate = excluded.bitrate,
                sample_rate = excluded.sample_rate,
                bits_per_sample = excluded.bits_per_sample,
                channels = excluded.channels,
                format = excluded.format,
                date_modified = excluded.date_modified;
        ";

        var pFilePath = cmd.Parameters.Add("$filePath", SqliteType.Text);
        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pArtist = cmd.Parameters.Add("$artist", SqliteType.Text);
        var pAlbum = cmd.Parameters.Add("$album", SqliteType.Text);
        var pYear = cmd.Parameters.Add("$year", SqliteType.Integer);
        var pTrackNumber = cmd.Parameters.Add("$trackNumber", SqliteType.Integer);
        var pGenre = cmd.Parameters.Add("$genre", SqliteType.Text);
        var pDurationMs = cmd.Parameters.Add("$durationMs", SqliteType.Integer);
        var pBitrate = cmd.Parameters.Add("$bitrate", SqliteType.Integer);
        var pSampleRate = cmd.Parameters.Add("$sampleRate", SqliteType.Integer);
        var pBitsPerSample = cmd.Parameters.Add("$bitsPerSample", SqliteType.Integer);
        var pChannels = cmd.Parameters.Add("$channels", SqliteType.Integer);
        var pFormat = cmd.Parameters.Add("$format", SqliteType.Text);
        var pDateAdded = cmd.Parameters.Add("$dateAdded", SqliteType.Text);
        var pDateModified = cmd.Parameters.Add("$dateModified", SqliteType.Text);

        foreach (var track in tracks)
        {
            pFilePath.Value = track.FilePath;
            pTitle.Value = track.Title ?? "Unknown Title";
            pArtist.Value = track.Artist ?? "Unknown Artist";
            pAlbum.Value = track.Album ?? "Unknown Album";
            pYear.Value = (long)track.Year;
            pTrackNumber.Value = (long)track.TrackNumber;
            pGenre.Value = track.Genre ?? "";
            pDurationMs.Value = (long)track.Duration.TotalMilliseconds;
            pBitrate.Value = track.Bitrate;
            pSampleRate.Value = track.SampleRate;
            pBitsPerSample.Value = track.BitsPerSample;
            pChannels.Value = track.Channels;
            pFormat.Value = track.FileFormat ?? "FLAC";
            pDateAdded.Value = track.DateAdded.ToString("O");
            pDateModified.Value = track.DateModified.ToString("O");

            await cmd.ExecuteNonQueryAsync();
        }

        await trans.CommitAsync();
    }

    public async Task<Track?> GetTrackByPathAsync(string filePath)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE file_path = $filePath LIMIT 1;";
        cmd.Parameters.AddWithValue("$filePath", filePath);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTrack(reader);
        }

        return null;
    }

    public async Task<List<Track>> GetAllTracksAsync()
    {
        var list = new List<Track>();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks ORDER BY artist COLLATE NOCASE, album COLLATE NOCASE, track_number, title COLLATE NOCASE;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadTrack(reader));
        }

        return list;
    }

    public async Task<List<Track>> GetFilteredTracksAsync(string sortBy = "Name", string? genreFilter = null, string? searchQuery = null)
    {
        var list = new List<Track>();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(genreFilter) && genreFilter != "Todos los géneros")
        {
            conditions.Add("genre = $genre");
            cmd.Parameters.AddWithValue("$genre", genreFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            conditions.Add("(title LIKE $q OR artist LIKE $q OR album LIKE $q OR genre LIKE $q)");
            cmd.Parameters.AddWithValue("$q", $"%{searchQuery}%");
        }

        string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        string orderClause = sortBy switch
        {
            "Album" => "ORDER BY album COLLATE NOCASE, track_number, title COLLATE NOCASE",
            "Duration" => "ORDER BY duration_ms DESC",
            "DateModified" => "ORDER BY date_modified DESC",
            "DateAdded" => "ORDER BY date_added DESC",
            "Genre" => "ORDER BY genre COLLATE NOCASE, artist COLLATE NOCASE",
            _ => "ORDER BY title COLLATE NOCASE, artist COLLATE NOCASE"
        };

        cmd.CommandText = $"SELECT * FROM tracks {whereClause} {orderClause};";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadTrack(reader));
        }

        return list;
    }

    public async Task<List<string>> GetGenresAsync()
    {
        var list = new List<string> { "Todos los géneros" };
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT genre FROM tracks WHERE genre IS NOT NULL AND TRIM(genre) != '' ORDER BY genre COLLATE NOCASE;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var g = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(g) && !list.Contains(g))
            {
                list.Add(g);
            }
        }

        return list;
    }

    public async Task<List<Track>> SearchTracksAsync(string query)
    {
        return await GetFilteredTracksAsync("Name", null, query);
    }

    public async Task RecordPlayAsync(long trackId)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd1 = conn.CreateCommand())
            {
                cmd1.Transaction = tx;
                cmd1.CommandText = "UPDATE tracks SET play_count = play_count + 1 WHERE id = $id;";
                cmd1.Parameters.AddWithValue("$id", trackId);
                await cmd1.ExecuteNonQueryAsync();
            }

            using (var cmd2 = conn.CreateCommand())
            {
                cmd2.Transaction = tx;
                cmd2.CommandText = "INSERT INTO playback_history (track_id, played_at) VALUES ($trackId, $playedAt);";
                cmd2.Parameters.AddWithValue("$trackId", trackId);
                cmd2.Parameters.AddWithValue("$playedAt", DateTime.UtcNow.ToString("O"));
                await cmd2.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
        }
    }

    public async Task RecordRecentPlayAsync(Track track)
    {
        if (track == null) return;
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();

            // 1. Check most recent entry
            long lastTrackId = -1;
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.Transaction = tx;
                checkCmd.CommandText = "SELECT track_id FROM recent_plays ORDER BY played_at_timestamp DESC LIMIT 1;";
                var res = await checkCmd.ExecuteScalarAsync();
                if (res != null && long.TryParse(res.ToString(), out var tid))
                {
                    lastTrackId = tid;
                }
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (lastTrackId == track.Id)
            {
                // Update timestamp of consecutive duplicate
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = @"
                    UPDATE recent_plays
                    SET played_at_timestamp = $ts
                    WHERE id = (SELECT id FROM recent_plays ORDER BY played_at_timestamp DESC LIMIT 1);";
                updateCmd.Parameters.AddWithValue("$ts", now);
                await updateCmd.ExecuteNonQueryAsync();
            }
            else
            {
                // Insert new entry
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO recent_plays (track_id, album_name, played_at_timestamp)
                    VALUES ($trackId, $albumName, $ts);";
                insertCmd.Parameters.AddWithValue("$trackId", track.Id);
                insertCmd.Parameters.AddWithValue("$albumName", track.Album ?? "");
                insertCmd.Parameters.AddWithValue("$ts", now);
                await insertCmd.ExecuteNonQueryAsync();

                // Trim to last 50 entries
                using var trimCmd = conn.CreateCommand();
                trimCmd.Transaction = tx;
                trimCmd.CommandText = @"
                    DELETE FROM recent_plays
                    WHERE id NOT IN (
                        SELECT id FROM recent_plays ORDER BY played_at_timestamp DESC LIMIT 50
                    );";
                await trimCmd.ExecuteNonQueryAsync();
            }

            // Also increment play count in tracks table
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.Transaction = tx;
                countCmd.CommandText = "UPDATE tracks SET play_count = play_count + 1 WHERE id = $id;";
                countCmd.Parameters.AddWithValue("$id", track.Id);
                await countCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error recording recent play: {ex.Message}");
        }
    }

    public async Task<List<Track>> GetRecentHistoryAsync(int limit = 50)
    {
        var list = new List<Track>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.* FROM recent_plays rp
                JOIN tracks t ON rp.track_id = t.id
                ORDER BY rp.played_at_timestamp DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadTrack(reader));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting recent history: {ex.Message}");
        }

        return list;
    }

    // Folders management
    public async Task<List<string>> GetLibraryFoldersAsync()
    {
        var list = new List<string>();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT folder_path FROM library_folders ORDER BY added_at DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task AddLibraryFolderAsync(string folderPath)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO library_folders (folder_path, added_at)
            VALUES ($path, $added)
            ON CONFLICT(folder_path) DO NOTHING;
        ";
        cmd.Parameters.AddWithValue("$path", folderPath);
        cmd.Parameters.AddWithValue("$added", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveLibraryFolderAsync(string folderPath)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM library_folders WHERE folder_path = $path;";
        cmd.Parameters.AddWithValue("$path", folderPath);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveLibraryFolderAndTracksAsync(string folderPath)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM library_folders WHERE folder_path = $path;";
            cmd.Parameters.AddWithValue("$path", folderPath);
            await cmd.ExecuteNonQueryAsync();

            string normalizedPrefix = folderPath.TrimEnd('\\', '/') + "%";
            using var cmdTracks = conn.CreateCommand();
            cmdTracks.Transaction = tx;
            cmdTracks.CommandText = "DELETE FROM tracks WHERE file_path LIKE $prefix;";
            cmdTracks.Parameters.AddWithValue("$prefix", normalizedPrefix);
            await cmdTracks.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static Track ReadTrack(SqliteDataReader reader)
    {
        DateTime dateAdded = DateTime.UtcNow;
        DateTime dateModified = DateTime.UtcNow;

        try
        {
            if (!reader.IsDBNull(reader.GetOrdinal("date_added")))
            {
                DateTime.TryParse(reader.GetString(reader.GetOrdinal("date_added")), out dateAdded);
            }
            if (!reader.IsDBNull(reader.GetOrdinal("date_modified")))
            {
                DateTime.TryParse(reader.GetString(reader.GetOrdinal("date_modified")), out dateModified);
            }
        }
        catch { }
        bool isFav = false;
        try
        {
            int favOrd = reader.GetOrdinal("is_favorite");
            if (favOrd >= 0 && !reader.IsDBNull(favOrd))
                isFav = reader.GetInt32(favOrd) == 1;
        }
        catch { }

        int playCount = 0;
        try
        {
            int pcOrd = reader.GetOrdinal("play_count");
            if (pcOrd >= 0 && !reader.IsDBNull(pcOrd))
                playCount = reader.GetInt32(pcOrd);
        }
        catch { }

        string albumArtist = "";
        try
        {
            int aaOrd = reader.GetOrdinal("album_artist");
            if (aaOrd >= 0 && !reader.IsDBNull(aaOrd))
                albumArtist = reader.GetString(aaOrd);
        }
        catch { }

        string comments = "";
        try
        {
            int commOrd = reader.GetOrdinal("comments");
            if (commOrd >= 0 && !reader.IsDBNull(commOrd))
                comments = reader.GetString(commOrd);
        }
        catch { }

        long fileSize = 0;
        try
        {
            int fsOrd = reader.GetOrdinal("file_size");
            if (fsOrd >= 0 && !reader.IsDBNull(fsOrd))
                fileSize = reader.GetInt64(fsOrd);
        }
        catch { }

        return new Track
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            Title = reader.IsDBNull(reader.GetOrdinal("title")) ? "Unknown" : reader.GetString(reader.GetOrdinal("title")),
            Artist = reader.IsDBNull(reader.GetOrdinal("artist")) ? "Unknown" : reader.GetString(reader.GetOrdinal("artist")),
            Album = reader.IsDBNull(reader.GetOrdinal("album")) ? "Unknown" : reader.GetString(reader.GetOrdinal("album")),
            Year = reader.IsDBNull(reader.GetOrdinal("year")) ? 0 : (uint)reader.GetInt64(reader.GetOrdinal("year")),
            TrackNumber = reader.IsDBNull(reader.GetOrdinal("track_number")) ? 0 : (uint)reader.GetInt64(reader.GetOrdinal("track_number")),
            Genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? "" : reader.GetString(reader.GetOrdinal("genre")),
            Duration = TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("duration_ms"))),
            Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),
            SampleRate = reader.GetInt32(reader.GetOrdinal("sample_rate")),
            BitsPerSample = reader.GetInt32(reader.GetOrdinal("bits_per_sample")),
            Channels = reader.GetInt32(reader.GetOrdinal("channels")),
            FileFormat = reader.IsDBNull(reader.GetOrdinal("format")) ? "FLAC" : reader.GetString(reader.GetOrdinal("format")),
            DateAdded = dateAdded,
            DateModified = dateModified,
            IsFavorite = isFav,
            PlayCount = playCount,
            AlbumArtist = albumArtist,
            Comments = comments,
            FileSize = fileSize
        };
    }

    public async Task SetTrackFavoriteAsync(long trackId, bool isFavorite)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET is_favorite = $fav WHERE id = $id;";
            cmd.Parameters.AddWithValue("$fav", isFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", trackId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    public async Task<List<Track>> GetFavoriteTracksAsync()
    {
        var list = new List<Track>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracks WHERE is_favorite = 1 ORDER BY title COLLATE NOCASE;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadTrack(reader));
            }
        }
        catch { }
        return list;
    }

    public async Task<List<Track>> GetTopTracksAsync(string period, int limit = 20)
    {
        var list = new List<Track>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$limit", limit);

            if (period == "Month")
            {
                cmd.CommandText = @"
                    SELECT t.*, COUNT(ph.id) as period_plays
                    FROM tracks t
                    JOIN playback_history ph ON t.id = ph.track_id
                    WHERE ph.played_at >= date('now', 'start of month')
                    GROUP BY t.id
                    ORDER BY period_plays DESC, t.play_count DESC
                    LIMIT $limit;";
            }
            else if (period == "Year")
            {
                cmd.CommandText = @"
                    SELECT t.*, COUNT(ph.id) as period_plays
                    FROM tracks t
                    JOIN playback_history ph ON t.id = ph.track_id
                    WHERE ph.played_at >= date('now', 'start of year')
                    GROUP BY t.id
                    ORDER BY period_plays DESC, t.play_count DESC
                    LIMIT $limit;";
            }
            else
            {
                cmd.CommandText = @"
                    SELECT * FROM tracks
                    ORDER BY play_count DESC, title COLLATE NOCASE
                    LIMIT $limit;";
            }

            using var reader = await cmd.ExecuteReaderAsync();
            int rank = 1;
            while (await reader.ReadAsync())
            {
                var tr = ReadTrack(reader);
                tr.Rank = rank++;
                list.Add(tr);
            }

            // Fallback if no history records exist yet
            if (list.Count == 0)
            {
                using var fbCmd = conn.CreateCommand();
                fbCmd.CommandText = "SELECT * FROM tracks ORDER BY play_count DESC, id ASC LIMIT $limit;";
                fbCmd.Parameters.AddWithValue("$limit", limit);
                using var fbReader = await fbCmd.ExecuteReaderAsync();
                rank = 1;
                while (await fbReader.ReadAsync())
                {
                    var tr = ReadTrack(fbReader);
                    tr.Rank = rank++;
                    list.Add(tr);
                }
            }
        }
        catch { }
        return list;
    }

    public async Task<List<Track>> GetForgottenGemsAsync(int limit = 20)
    {
        var list = new List<Track>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM tracks
                ORDER BY play_count ASC, RANDOM()
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadTrack(reader));
            }
        }
        catch { }
        return list;
    }

    public async Task<List<string>> GetTopGenresAsync(int limit = 3)
    {
        var list = new List<string>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT genre, COUNT(*) as cnt
                FROM tracks
                WHERE genre IS NOT NULL AND TRIM(genre) != '' AND genre != 'Unknown'
                GROUP BY genre
                ORDER BY cnt DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }
        }
        catch { }
        return list;
    }

    public async Task<List<Track>> GetTracksByGenreAsync(string genre, int limit = 30)
    {
        var list = new List<Track>();
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracks WHERE genre = $g ORDER BY RANDOM() LIMIT $limit;";
            cmd.Parameters.AddWithValue("$g", genre);
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadTrack(reader));
            }
        }
        catch { }
        return list;
    }

    public async Task DeleteTrackAsync(long trackId)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM tracks WHERE id = $id;
                DELETE FROM recent_plays WHERE track_id = $id;
                DELETE FROM playback_history WHERE track_id = $id;
                DELETE FROM playlist_tracks WHERE track_id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", trackId);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch { }
    }

    public async Task<string> GetSettingAsync(string key, string defaultValue = "")
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_settings WHERE key = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", key);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SetSettingAsync(string key, string value)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO app_settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            ";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }
}
