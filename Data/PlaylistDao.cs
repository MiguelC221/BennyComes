using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenyComes.Models;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

public class PlaylistDao
{
    private static readonly Lazy<PlaylistDao> _lazy = new(() => new PlaylistDao());
    public static PlaylistDao Instance => _lazy.Value;

    private readonly DatabaseContext _db = DatabaseContext.Instance;

    private PlaylistDao() { }

    public async Task<List<Playlist>> GetAllPlaylistsAsync()
    {
        var list = new List<Playlist>();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT p.id, p.name, p.created_at, p.description, p.custom_cover_path, COUNT(pt.track_id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON p.id = pt.playlist_id
            GROUP BY p.id, p.name, p.created_at
            ORDER BY p.name COLLATE NOCASE;
        ";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var p = new Playlist
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("created_at")), out var dt) ? dt : DateTime.UtcNow,
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString(reader.GetOrdinal("description")),
                CustomCoverPath = reader.IsDBNull(reader.GetOrdinal("custom_cover_path")) ? null : reader.GetString(reader.GetOrdinal("custom_cover_path")),
                TrackCount = reader.GetInt32(reader.GetOrdinal("track_count"))
            };
            p.RefreshCoverImage();
            list.Add(p);
        }

        return list;
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, IEnumerable<Track>? tracks = null)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO playlists (name, created_at) VALUES ($name, $created) RETURNING id;";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));

            var newIdObj = await cmd.ExecuteScalarAsync();
            var playlistId = Convert.ToInt64(newIdObj);

            var playlist = new Playlist
            {
                Id = playlistId,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };

            if (tracks != null)
            {
                int pos = 0;
                foreach (var t in tracks)
                {
                    if (t.Id <= 0)
                    {
                        await LibraryDao.Instance.InsertOrUpdateTrackAsync(t);
                    }

                    using var trackCmd = conn.CreateCommand();
                    trackCmd.Transaction = tx;
                    trackCmd.CommandText = @"
                        INSERT INTO playlist_tracks (playlist_id, track_id, position)
                        VALUES ($playlistId, $trackId, $pos);
                    ";
                    trackCmd.Parameters.AddWithValue("$playlistId", playlistId);
                    trackCmd.Parameters.AddWithValue("$trackId", t.Id);
                    trackCmd.Parameters.AddWithValue("$pos", pos++);
                    await trackCmd.ExecuteNonQueryAsync();

                    playlist.Tracks.Add(t);
                }
            }

            playlist.TrackCount = playlist.Tracks.Count;
            await tx.CommitAsync();
            return playlist;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeletePlaylistAsync(long playlistId)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", playlistId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Track>> GetPlaylistTracksAsync(long playlistId)
    {
        var list = new List<Track>();
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT t.*
            FROM playlist_tracks pt
            JOIN tracks t ON pt.track_id = t.id
            WHERE pt.playlist_id = $pid
            ORDER BY pt.position ASC;
        ";
        cmd.Parameters.AddWithValue("$pid", playlistId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Track
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
                FileFormat = reader.IsDBNull(reader.GetOrdinal("format")) ? "FLAC" : reader.GetString(reader.GetOrdinal("format"))
            });
        }

        return list;
    }

    public async Task AddTrackToPlaylistAsync(long playlistId, long trackId)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO playlist_tracks (playlist_id, track_id, position)
            VALUES ($playlistId, $trackId, (SELECT COALESCE(MAX(position), -1) + 1 FROM playlist_tracks WHERE playlist_id = $playlistId));
        ";
        cmd.Parameters.AddWithValue("$playlistId", playlistId);
        cmd.Parameters.AddWithValue("$trackId", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetTrackCountInPlaylistAsync(long playlistId, long trackId)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM playlist_tracks WHERE playlist_id = $pid AND track_id = $tid;";
        cmd.Parameters.AddWithValue("$pid", playlistId);
        cmd.Parameters.AddWithValue("$tid", trackId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM playlist_tracks 
            WHERE rowid IN (SELECT rowid FROM playlist_tracks WHERE playlist_id = $pid AND track_id = $tid LIMIT 1);
        ";
        cmd.Parameters.AddWithValue("$pid", playlistId);
        cmd.Parameters.AddWithValue("$tid", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateTrackPositionsAsync(long playlistId, IList<Track> tracks)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE playlist_tracks SET position = $pos WHERE playlist_id = $pid AND track_id = $tid;";
                cmd.Parameters.AddWithValue("$pos", i);
                cmd.Parameters.AddWithValue("$pid", playlistId);
                cmd.Parameters.AddWithValue("$tid", tracks[i].Id);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task UpdatePlaylistMetadataAsync(long playlistId, string name, string description, string? customCoverPath)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE playlists 
            SET name = $name, description = $desc, custom_cover_path = $cover
            WHERE id = $id;
        ";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$desc", description ?? "");
        cmd.Parameters.AddWithValue("$cover", (object?)customCoverPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", playlistId);
        await cmd.ExecuteNonQueryAsync();
    }
}
