using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

/// <summary>
/// Clase Favoritos con base de datos SQLite propia (favorites.db) para gestionar
/// canciones favoritas tanto de la Biblioteca normal como de osu! library.
/// </summary>
public sealed class Favoritos
{
    private static readonly Lazy<Favoritos> _instance = new(() => new Favoritos());
    public static Favoritos Instance => _instance.Value;

    private readonly string _connectionString;
    private readonly object _syncLock = new();
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Track> _favoriteTracksByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized;

    public event Action? FavoritesUpdated;

    public bool CombineLibraries { get; private set; }

    private Favoritos()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "MusicPlayer");
        Directory.CreateDirectory(appFolder);

        var dbPath = Path.Combine(appFolder, "favorites.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();

        InitializeDatabase();
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void InitializeDatabase()
    {
        try
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS favorite_tracks (
                    file_path TEXT PRIMARY KEY COLLATE NOCASE,
                    track_id INTEGER NOT NULL DEFAULT 0,
                    is_osu INTEGER NOT NULL DEFAULT 0,
                    title TEXT NOT NULL DEFAULT '',
                    artist TEXT NOT NULL DEFAULT '',
                    album TEXT NOT NULL DEFAULT '',
                    genre TEXT NOT NULL DEFAULT '',
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    artwork_path TEXT NOT NULL DEFAULT '',
                    video_path TEXT NOT NULL DEFAULT '',
                    video_offset_ms INTEGER NOT NULL DEFAULT 0,
                    osu_source TEXT NOT NULL DEFAULT '',
                    osu_artist_roumaji TEXT NOT NULL DEFAULT '',
                    osu_title_roumaji TEXT NOT NULL DEFAULT '',
                    osu_artist_unicode TEXT NOT NULL DEFAULT '',
                    osu_title_unicode TEXT NOT NULL DEFAULT '',
                    date_favorited TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_fav_is_osu ON favorite_tracks(is_osu);
                CREATE INDEX IF NOT EXISTS idx_fav_date ON favorite_tracks(date_favorited DESC);

                CREATE TABLE IF NOT EXISTS favorite_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();

            LoadFromDatabaseSync(conn);
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Favoritos] Error initializing favorites.db: {ex.Message}");
        }
    }

    private void LoadFromDatabaseSync(SqliteConnection conn)
    {
        lock (_syncLock)
        {
            _favoritePaths.Clear();
            _favoriteTracksByPath.Clear();

            using (var setCmd = conn.CreateCommand())
            {
                setCmd.CommandText = "SELECT value FROM favorite_settings WHERE key = 'combine_libraries' LIMIT 1;";
                var val = setCmd.ExecuteScalar()?.ToString();
                CombineLibraries = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT file_path, track_id, is_osu, title, artist, album, genre, duration_ms,
                       artwork_path, video_path, video_offset_ms, osu_source,
                       osu_artist_roumaji, osu_title_roumaji, osu_artist_unicode, osu_title_unicode,
                       date_favorited
                FROM favorite_tracks
                ORDER BY title COLLATE NOCASE ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string filePath = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(filePath)) continue;

                long trackId = reader.GetInt64(1);
                bool isOsu = reader.GetInt32(2) == 1;
                string title = reader.GetString(3);
                string artist = reader.GetString(4);
                string album = reader.GetString(5);
                string genre = reader.GetString(6);
                long durationMs = reader.GetInt64(7);
                string artworkPath = reader.GetString(8);
                string videoPath = reader.GetString(9);
                int videoOffsetMs = reader.GetInt32(10);
                string osuSource = reader.GetString(11);
                string osuArtistRoumaji = reader.GetString(12);
                string osuTitleRoumaji = reader.GetString(13);
                string osuArtistUnicode = reader.GetString(14);
                string osuTitleUnicode = reader.GetString(15);

                var track = new Track
                {
                    Id = trackId > 0 ? trackId : (isOsu ? Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(filePath)) + 1_000_000L : 0),
                    FilePath = filePath,
                    Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(filePath) : title,
                    Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist,
                    Album = string.IsNullOrWhiteSpace(album) ? (isOsu ? title : "Unknown Album") : album,
                    Genre = isOsu ? "osu!" : (string.IsNullOrWhiteSpace(genre) ? "Unknown" : genre),
                    Duration = TimeSpan.FromMilliseconds(Math.Max(0, durationMs)),
                    ArtworkPath = artworkPath ?? string.Empty,
                    VideoPath = videoPath ?? string.Empty,
                    VideoOffsetMs = videoOffsetMs,
                    OsuSource = osuSource ?? string.Empty,
                    OsuArtistRoumaji = osuArtistRoumaji ?? string.Empty,
                    OsuTitleRoumaji = osuTitleRoumaji ?? string.Empty,
                    OsuArtistUnicode = osuArtistUnicode ?? string.Empty,
                    OsuTitleUnicode = osuTitleUnicode ?? string.Empty,
                    FirstLetterGroup = Helpers.AlphaGroupHelper.GetAlphaGroupKey(title),
                    IsFavorite = true
                };

                _favoritePaths.Add(filePath);
                _favoriteTracksByPath[filePath] = track;
            }
        }
    }

    public async Task InitializeAndMigrateAsync()
    {
        if (!_isInitialized)
        {
            InitializeDatabase();
        }

        // Migración inicial: si hay pistas marcadas como favoritas en library.db que no están en favorites.db, importarlas
        try
        {
            var legacyFavs = await LibraryDao.Instance.GetFavoriteTracksAsync();
            bool addedAny = false;
            foreach (var t in legacyFavs)
            {
                if (!string.IsNullOrWhiteSpace(t.FilePath) && !IsFavorite(t.FilePath))
                {
                    t.IsFavorite = true;
                    await UpsertFavoriteInDbAsync(t);
                    lock (_syncLock)
                    {
                        _favoritePaths.Add(t.FilePath);
                        _favoriteTracksByPath[t.FilePath] = t;
                    }
                    addedAny = true;
                }
            }

            SyncAllLoadedTracks();
            if (addedAny)
            {
                FavoritesUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Favoritos] Migration warning: {ex.Message}");
        }
    }

    public static bool IsOsuTrack(Track? track)
    {
        if (track == null) return false;
        if (string.Equals(track.Genre, "osu!", StringComparison.OrdinalIgnoreCase)) return true;
        if (track.Id >= 1_000_000) return true;
        if (!string.IsNullOrEmpty(track.OsuArtistRoumaji) || !string.IsNullOrEmpty(track.OsuTitleRoumaji)) return true;
        return false;
    }

    public bool IsFavorite(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        lock (_syncLock)
        {
            return _favoritePaths.Contains(filePath);
        }
    }

    public async Task SetCombineLibrariesAsync(bool combine)
    {
        CombineLibraries = combine;
        await Task.Run(() =>
        {
            try
            {
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO favorite_settings (key, value)
                    VALUES ('combine_libraries', $val)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                cmd.Parameters.AddWithValue("$val", combine ? "true" : "false");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Favoritos] Error saving combine_libraries: {ex.Message}");
            }
        });
    }

    public async Task<bool> ToggleFavoriteAsync(Track? track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath)) return false;

        bool newState;
        lock (_syncLock)
        {
            newState = !_favoritePaths.Contains(track.FilePath);
            if (newState)
            {
                _favoritePaths.Add(track.FilePath);
                _favoriteTracksByPath[track.FilePath] = track;
            }
            else
            {
                _favoritePaths.Remove(track.FilePath);
                _favoriteTracksByPath.Remove(track.FilePath);
            }
        }

        track.IsFavorite = newState;

        if (newState)
        {
            await UpsertFavoriteInDbAsync(track);
        }
        else
        {
            await RemoveFavoriteFromDbAsync(track.FilePath);
        }

        // Si es pista de la biblioteca normal, sincronizar también con library.db por compatibilidad
        if (!IsOsuTrack(track) && track.Id > 0)
        {
            try
            {
                await LibraryDao.Instance.SetTrackFavoriteAsync(track.Id, newState);
            }
            catch { }
        }

        PropagateFavoriteState(track.FilePath, newState);
        FavoritesUpdated?.Invoke();
        return newState;
    }

    private async Task UpsertFavoriteInDbAsync(Track track)
    {
        await Task.Run(() =>
        {
            try
            {
                bool isOsu = IsOsuTrack(track);
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO favorite_tracks (
                        file_path, track_id, is_osu, title, artist, album, genre, duration_ms,
                        artwork_path, video_path, video_offset_ms, osu_source,
                        osu_artist_roumaji, osu_title_roumaji, osu_artist_unicode, osu_title_unicode,
                        date_favorited
                    ) VALUES (
                        $path, $id, $osu, $title, $artist, $album, $genre, $dur,
                        $art, $vid, $vidoff, $src,
                        $aroum, $troum, $auni, $tuni,
                        $date
                    )
                    ON CONFLICT(file_path) DO UPDATE SET
                        track_id = excluded.track_id,
                        is_osu = excluded.is_osu,
                        title = excluded.title,
                        artist = excluded.artist,
                        album = excluded.album,
                        genre = excluded.genre,
                        duration_ms = excluded.duration_ms,
                        artwork_path = excluded.artwork_path,
                        video_path = excluded.video_path,
                        video_offset_ms = excluded.video_offset_ms,
                        osu_source = excluded.osu_source,
                        osu_artist_roumaji = excluded.osu_artist_roumaji,
                        osu_title_roumaji = excluded.osu_title_roumaji,
                        osu_artist_unicode = excluded.osu_artist_unicode,
                        osu_title_unicode = excluded.osu_title_unicode;";

                cmd.Parameters.AddWithValue("$path", track.FilePath);
                cmd.Parameters.AddWithValue("$id", track.Id);
                cmd.Parameters.AddWithValue("$osu", isOsu ? 1 : 0);
                cmd.Parameters.AddWithValue("$title", track.Title ?? string.Empty);
                cmd.Parameters.AddWithValue("$artist", track.Artist ?? string.Empty);
                cmd.Parameters.AddWithValue("$album", track.Album ?? string.Empty);
                cmd.Parameters.AddWithValue("$genre", isOsu ? "osu!" : (track.Genre ?? string.Empty));
                cmd.Parameters.AddWithValue("$dur", (long)track.Duration.TotalMilliseconds);
                cmd.Parameters.AddWithValue("$art", track.ArtworkPath ?? string.Empty);
                cmd.Parameters.AddWithValue("$vid", track.VideoPath ?? string.Empty);
                cmd.Parameters.AddWithValue("$vidoff", track.VideoOffsetMs);
                cmd.Parameters.AddWithValue("$src", track.OsuSource ?? string.Empty);
                cmd.Parameters.AddWithValue("$aroum", track.OsuArtistRoumaji ?? string.Empty);
                cmd.Parameters.AddWithValue("$troum", track.OsuTitleRoumaji ?? string.Empty);
                cmd.Parameters.AddWithValue("$auni", track.OsuArtistUnicode ?? string.Empty);
                cmd.Parameters.AddWithValue("$tuni", track.OsuTitleUnicode ?? string.Empty);
                cmd.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Favoritos] Error upserting favorite: {ex.Message}");
            }
        });
    }

    private async Task RemoveFavoriteFromDbAsync(string filePath)
    {
        await Task.Run(() =>
        {
            try
            {
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM favorite_tracks WHERE file_path = $path;";
                cmd.Parameters.AddWithValue("$path", filePath);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Favoritos] Error deleting favorite: {ex.Message}");
            }
        });
    }

    public void SyncTracksFavoriteState(IEnumerable<Track>? tracks)
    {
        if (tracks == null) return;
        lock (_syncLock)
        {
            foreach (var t in tracks)
            {
                if (string.IsNullOrWhiteSpace(t.FilePath)) continue;
                bool fav = _favoritePaths.Contains(t.FilePath);
                if (t.IsFavorite != fav)
                {
                    t.IsFavorite = fav;
                }
                if (fav)
                {
                    // Prefer the live in-memory Track instance so artwork/playing states stay shared
                    _favoriteTracksByPath[t.FilePath] = t;
                }
            }
        }
    }

    public void SyncAllLoadedTracks()
    {
        try
        {
            SyncTracksFavoriteState(LibraryCacheService.Instance.CachedTracks);
            SyncTracksFavoriteState(OsuLibraryService.Instance.Tracks);
            SyncTracksFavoriteState(AudioPlayerService.Instance.Queue);
            if (AudioPlayerService.Instance.CurrentTrack != null)
            {
                SyncTracksFavoriteState(new[] { AudioPlayerService.Instance.CurrentTrack });
            }
        }
        catch { }
    }

    private void PropagateFavoriteState(string filePath, bool isFav)
    {
        void UpdateCollection(IEnumerable<Track>? col)
        {
            if (col == null) return;
            foreach (var t in col)
            {
                if (string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (t.IsFavorite != isFav)
                        t.IsFavorite = isFav;
                }
            }
        }

        try
        {
            UpdateCollection(LibraryCacheService.Instance.CachedTracks);
            UpdateCollection(OsuLibraryService.Instance.Tracks);
            UpdateCollection(AudioPlayerService.Instance.Queue);

            var cur = AudioPlayerService.Instance.CurrentTrack;
            if (cur != null && string.Equals(cur.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                cur.IsFavorite = isFav;
            }

            var vm = ViewModels.MainViewModel.Instance;
            if (vm != null)
            {
                if (vm.CurrentTrack != null && string.Equals(vm.CurrentTrack.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    vm.CurrentTrack.IsFavorite = isFav;
                }
                UpdateCollection(vm.SelectedPlaylistForDetail?.Tracks);
            }
        }
        catch { }
    }

    public List<Track> GetLibraryFavoriteTracks()
    {
        SyncAllLoadedTracks();
        lock (_syncLock)
        {
            return _favoriteTracksByPath.Values
                .Where(t => !IsOsuTrack(t))
                .OrderBy(t => t.GroupRank)
                .ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public List<Track> GetOsuFavoriteTracks()
    {
        SyncAllLoadedTracks();
        lock (_syncLock)
        {
            return _favoriteTracksByPath.Values
                .Where(IsOsuTrack)
                .OrderBy(t => t.GroupRank)
                .ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public List<Track> GetAllFavoriteTracks()
    {
        SyncAllLoadedTracks();
        lock (_syncLock)
        {
            return _favoriteTracksByPath.Values
                .OrderBy(t => t.GroupRank)
                .ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
