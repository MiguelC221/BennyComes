using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services.Library;
using BenyComes.Services.Metadata;
using Microsoft.Data.Sqlite;

namespace BenyComes.Data;

/// <summary>
/// High-performance partitioned index database engine stored in %LocalAppData%\BenyComes\IndexDB\.
/// Splits storage into dedicated SQLite WAL databases (songs_index.db, albums_index.db, artists_index.db, search_index.db)
/// to allow lock-free parallel reads/writes during progressive indexing and instantaneous startup & search.
/// </summary>
public sealed class LibraryIndexDatabase
{
    private static readonly Lazy<LibraryIndexDatabase> _lazy = new(() => new LibraryIndexDatabase());
    public static LibraryIndexDatabase Instance => _lazy.Value;

    public string IndexDirectory { get; }
    private readonly string _songsConnStr;
    private readonly string _albumsConnStr;
    private readonly string _artistsConnStr;
    private readonly string _searchConnStr;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly string ArtworkCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer",
        "ArtworkCache"
    );

    // Pre-checked artwork existence cache to avoid repeated disk I/O during indexing and startup
    private readonly ConcurrentDictionary<string, bool> _artworkExistsCache = new(StringComparer.OrdinalIgnoreCase);

    private LibraryIndexDatabase()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        IndexDirectory = Path.Combine(appData, "BenyComes", "IndexDB");
        Directory.CreateDirectory(IndexDirectory);

        _songsConnStr = BuildConnectionString(Path.Combine(IndexDirectory, "songs_index.db"));
        _albumsConnStr = BuildConnectionString(Path.Combine(IndexDirectory, "albums_index.db"));
        _artistsConnStr = BuildConnectionString(Path.Combine(IndexDirectory, "artists_index.db"));
        _searchConnStr = BuildConnectionString(Path.Combine(IndexDirectory, "search_index.db"));

        InitializeSchemas();
        WarmupArtworkDirectoryCache();
    }

    private static string BuildConnectionString(string dbPath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public void WarmupArtworkDirectoryCache()
    {
        try
        {
            if (!Directory.Exists(ArtworkCacheDir)) return;
            foreach (var file in Directory.EnumerateFiles(ArtworkCacheDir, "*.jpg"))
            {
                _artworkExistsCache[file] = true;
            }
        }
        catch { }
    }

    public bool IsArtworkCached(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return _artworkExistsCache.GetOrAdd(path, static p => File.Exists(p));
    }

    public void MarkArtworkCached(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _artworkExistsCache[path] = true;
        }
    }

    private void InitializeSchemas()
    {
        Parallel.Invoke(
            () =>
            {
                using var conn = new SqliteConnection(_songsConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA temp_store = MEMORY;
                    PRAGMA cache_size = -32000;
                    PRAGMA mmap_size = 134217728;

                    CREATE TABLE IF NOT EXISTS indexed_songs (
                        id INTEGER PRIMARY KEY,
                        file_path TEXT NOT NULL UNIQUE,
                        title TEXT NOT NULL,
                        artist TEXT NOT NULL,
                        album TEXT NOT NULL,
                        year INTEGER NOT NULL DEFAULT 0,
                        track_number INTEGER NOT NULL DEFAULT 0,
                        genre TEXT NOT NULL DEFAULT '',
                        duration_ms INTEGER NOT NULL DEFAULT 0,
                        bitrate INTEGER NOT NULL DEFAULT 0,
                        sample_rate INTEGER NOT NULL DEFAULT 44100,
                        bits_per_sample INTEGER NOT NULL DEFAULT 16,
                        channels INTEGER NOT NULL DEFAULT 2,
                        format TEXT NOT NULL DEFAULT 'FLAC',
                        date_added_ticks INTEGER NOT NULL DEFAULT 0,
                        date_modified_ticks INTEGER NOT NULL DEFAULT 0,
                        file_size INTEGER NOT NULL DEFAULT 0,
                        is_favorite INTEGER NOT NULL DEFAULT 0,
                        play_count INTEGER NOT NULL DEFAULT 0,
                        artwork_path TEXT NOT NULL DEFAULT '',
                        first_letter_group TEXT NOT NULL DEFAULT '#',
                        sort_title TEXT NOT NULL DEFAULT '',
                        group_rank INTEGER NOT NULL DEFAULT 0,
                        search_text TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_songs_rank_sort ON indexed_songs(group_rank, sort_title COLLATE NOCASE);
                    CREATE INDEX IF NOT EXISTS idx_songs_genre ON indexed_songs(genre COLLATE NOCASE);
                    CREATE INDEX IF NOT EXISTS idx_songs_path ON indexed_songs(file_path);
                ";
                cmd.ExecuteNonQuery();
            },
            () =>
            {
                using var conn = new SqliteConnection(_albumsConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA temp_store = MEMORY;
                    PRAGMA cache_size = -16000;
                    PRAGMA mmap_size = 67108864;

                    CREATE TABLE IF NOT EXISTS indexed_albums (
                        name TEXT PRIMARY KEY COLLATE NOCASE,
                        artist TEXT NOT NULL,
                        year INTEGER NOT NULL DEFAULT 0,
                        genre TEXT NOT NULL DEFAULT '',
                        artwork_path TEXT NOT NULL DEFAULT '',
                        alpha_group TEXT NOT NULL DEFAULT '#',
                        sort_key TEXT NOT NULL DEFAULT '',
                        group_rank INTEGER NOT NULL DEFAULT 0,
                        latest_date_ticks INTEGER NOT NULL DEFAULT 0,
                        track_count INTEGER NOT NULL DEFAULT 0,
                        track_ids_csv TEXT NOT NULL DEFAULT '',
                        search_text TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_albums_rank_sort ON indexed_albums(group_rank, sort_key COLLATE NOCASE);
                    CREATE INDEX IF NOT EXISTS idx_albums_date ON indexed_albums(latest_date_ticks DESC);
                ";
                cmd.ExecuteNonQuery();
            },
            () =>
            {
                using var conn = new SqliteConnection(_artistsConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA temp_store = MEMORY;
                    PRAGMA cache_size = -16000;
                    PRAGMA mmap_size = 67108864;

                    CREATE TABLE IF NOT EXISTS indexed_artists (
                        name TEXT PRIMARY KEY COLLATE NOCASE,
                        avatar_path TEXT NOT NULL DEFAULT '',
                        alpha_group TEXT NOT NULL DEFAULT '#',
                        sort_key TEXT NOT NULL DEFAULT '',
                        group_rank INTEGER NOT NULL DEFAULT 0,
                        track_count INTEGER NOT NULL DEFAULT 0,
                        album_count INTEGER NOT NULL DEFAULT 0,
                        track_ids_csv TEXT NOT NULL DEFAULT '',
                        album_names_csv TEXT NOT NULL DEFAULT '',
                        search_text TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_artists_rank_sort ON indexed_artists(group_rank, sort_key COLLATE NOCASE);
                ";
                cmd.ExecuteNonQuery();
            },
            () =>
            {
                using var conn = new SqliteConnection(_searchConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA temp_store = MEMORY;

                    CREATE TABLE IF NOT EXISTS search_entries (
                        entity_type TEXT NOT NULL,
                        entity_key TEXT NOT NULL,
                        display_title TEXT NOT NULL,
                        display_subtitle TEXT NOT NULL,
                        search_text TEXT NOT NULL,
                        PRIMARY KEY (entity_type, entity_key)
                    );
                    CREATE INDEX IF NOT EXISTS idx_search_entries_text ON search_entries(search_text);
                ";
                cmd.ExecuteNonQuery();

                try
                {
                    using var ftsCmd = conn.CreateCommand();
                    ftsCmd.CommandText = @"
                        CREATE VIRTUAL TABLE IF NOT EXISTS search_fts USING fts5(
                            entity_type UNINDEXED,
                            entity_key UNINDEXED,
                            search_text,
                            tokenize = 'unicode61 remove_diacritics 2',
                            prefix = '1 2 3'
                        );
                    ";
                    ftsCmd.ExecuteNonQuery();
                }
                catch { }
            }
        );
    }

    /// <summary>
    /// Loads Songs, Albums, and Artists concurrently from the 3 partitioned index databases in AppData.
    /// Returns false if the index DBs are empty (e.g., first run after upgrade) so caller can build them once.
    /// </summary>
    public async Task<(bool HasData, List<Track> Tracks, List<AlbumModel> Albums, List<ArtistModel> Artists, List<string> Genres)> TryLoadAllParallelAsync()
    {
        try
        {
            var songsTask = Task.Run(ReadIndexedSongs);
            var albumsTask = Task.Run(ReadIndexedAlbums);
            var artistsTask = Task.Run(ReadIndexedArtists);

            await Task.WhenAll(songsTask, albumsTask, artistsTask);

            var tracks = songsTask.Result;
            if (tracks.Count == 0)
            {
                return (false, new(), new(), new(), new());
            }

            var rawAlbums = albumsTask.Result;
            var rawArtists = artistsTask.Result;

            // Fast O(N) relational linking in memory
            var trackById = new Dictionary<long, Track>(tracks.Count);
            var genresSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tracks)
            {
                if (t.Id > 0) trackById[t.Id] = t;
                if (!string.IsNullOrWhiteSpace(t.Genre)) genresSet.Add(t.Genre);
            }

            var albumByName = new Dictionary<string, AlbumModel>(rawAlbums.Count, StringComparer.OrdinalIgnoreCase);
            var albums = new List<AlbumModel>(rawAlbums.Count);

            foreach (var (album, trackIdsCsv) in rawAlbums)
            {
                if (!string.IsNullOrEmpty(trackIdsCsv))
                {
                    var parts = trackIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var albumTracks = new List<Track>(parts.Length);
                    foreach (var p in parts)
                    {
                        if (long.TryParse(p, out long tid) && trackById.TryGetValue(tid, out var tr))
                        {
                            albumTracks.Add(tr);
                        }
                    }
                    album.Tracks = albumTracks;
                }
                albumByName[album.Name] = album;
                albums.Add(album);
            }

            var artists = new List<ArtistModel>(rawArtists.Count);
            foreach (var (artist, trackIdsCsv, albumNamesCsv) in rawArtists)
            {
                if (!string.IsNullOrEmpty(trackIdsCsv))
                {
                    var parts = trackIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var artistTracks = new List<Track>(parts.Length);
                    foreach (var p in parts)
                    {
                        if (long.TryParse(p, out long tid) && trackById.TryGetValue(tid, out var tr))
                        {
                            artistTracks.Add(tr);
                        }
                    }
                    artist.Tracks = artistTracks;
                }

                if (!string.IsNullOrEmpty(albumNamesCsv))
                {
                    var names = albumNamesCsv.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    var artistAlbums = new List<AlbumModel>(names.Length);
                    foreach (var n in names)
                    {
                        if (albumByName.TryGetValue(n, out var al))
                        {
                            artistAlbums.Add(al);
                        }
                    }
                    artist.Albums = artistAlbums;
                }

                artists.Add(artist);
            }

            // If albums or artists were somehow empty while tracks exist, trigger rebuild
            if (albums.Count == 0 || artists.Count == 0)
            {
                return (false, tracks, new(), new(), new());
            }

            var genres = new List<string>(genresSet.Count + 1) { "Todos los géneros" };
            genres.AddRange(genresSet.OrderBy(g => g, StringComparer.OrdinalIgnoreCase));

            return (true, tracks, albums, artists, genres);
        }
        catch
        {
            return (false, new(), new(), new(), new());
        }
    }

    private List<Track> ReadIndexedSongs()
    {
        var list = new List<Track>(1024);
        using var conn = new SqliteConnection(_songsConnStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, file_path, title, artist, album, year, track_number, genre,
                   duration_ms, bitrate, sample_rate, bits_per_sample, channels, format,
                   date_added_ticks, date_modified_ticks, file_size, is_favorite, play_count,
                   artwork_path, first_letter_group, sort_title, group_rank, search_text
            FROM indexed_songs
            ORDER BY group_rank ASC, sort_title COLLATE NOCASE ASC;
        ";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var title = r.GetString(2);
            var firstLetterGroup = r.GetString(20);
            var sortTitle = r.GetString(21);
            var groupRank = r.GetInt32(22);
            var searchText = r.GetString(23);

            var track = new Track
            {
                Id = r.GetInt64(0),
                FilePath = r.GetString(1),
                Title = title,
                Artist = r.GetString(3),
                Album = r.GetString(4),
                Year = (uint)r.GetInt64(5),
                TrackNumber = (uint)r.GetInt64(6),
                Genre = r.GetString(7),
                Duration = TimeSpan.FromMilliseconds(r.GetInt64(8)),
                Bitrate = r.GetInt32(9),
                SampleRate = r.GetInt32(10),
                BitsPerSample = r.GetInt32(11),
                Channels = r.GetInt32(12),
                FileFormat = r.GetString(13),
                DateAdded = new DateTime(r.GetInt64(14), DateTimeKind.Utc),
                DateModified = new DateTime(r.GetInt64(15), DateTimeKind.Utc),
                FileSize = r.GetInt64(16),
                IsFavorite = r.GetInt32(17) == 1,
                PlayCount = r.GetInt32(18),
                ArtworkPath = r.GetString(19),
                FirstLetterGroup = firstLetterGroup
            };
            track.SetPrecomputedSortAndSearch(sortTitle, groupRank, searchText);
            list.Add(track);
        }

        return list;
    }

    private List<(AlbumModel Album, string TrackIdsCsv)> ReadIndexedAlbums()
    {
        var list = new List<(AlbumModel, string)>(256);
        using var conn = new SqliteConnection(_albumsConnStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT name, artist, year, genre, artwork_path, alpha_group, sort_key,
                   group_rank, latest_date_ticks, track_count, track_ids_csv, search_text
            FROM indexed_albums
            ORDER BY group_rank ASC, sort_key COLLATE NOCASE ASC;
        ";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var artPath = r.GetString(4);
            var album = new AlbumModel
            {
                Name = r.GetString(0),
                Artist = r.GetString(1),
                Year = (uint)r.GetInt64(2),
                Genre = r.GetString(3),
                ArtworkPath = !string.IsNullOrEmpty(artPath) ? artPath : null,
                AlphaGroupKey = r.GetString(5),
                SortKey = r.GetString(6),
                GroupRank = r.GetInt32(7),
                LatestDateAdded = new DateTime(r.GetInt64(8), DateTimeKind.Utc),
                PersistedTrackCount = r.GetInt32(9),
                SearchText = r.GetString(11)
            };
            string csv = r.GetString(10);
            list.Add((album, csv));
        }

        return list;
    }

    private List<(ArtistModel Artist, string TrackIdsCsv, string AlbumNamesCsv)> ReadIndexedArtists()
    {
        var list = new List<(ArtistModel, string, string)>(256);
        using var conn = new SqliteConnection(_artistsConnStr);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT name, avatar_path, alpha_group, sort_key, group_rank,
                   track_count, album_count, track_ids_csv, album_names_csv, search_text
            FROM indexed_artists
            ORDER BY group_rank ASC, sort_key COLLATE NOCASE ASC;
        ";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var avatarPath = r.GetString(1);
            var artist = new ArtistModel
            {
                Name = r.GetString(0),
                AvatarPath = !string.IsNullOrEmpty(avatarPath) ? avatarPath : null,
                AlphaGroupKey = r.GetString(2),
                SortKey = r.GetString(3),
                GroupRank = r.GetInt32(4),
                PersistedTrackCount = r.GetInt32(5),
                PersistedAlbumCount = r.GetInt32(6),
                SearchText = r.GetString(9)
            };
            string trackCsv = r.GetString(7);
            string albumCsv = r.GetString(8);
            list.Add((artist, trackCsv, albumCsv));
        }

        return list;
    }

    /// <summary>
    /// Builds Albums and Artists in memory from a list of Tracks using cached artwork checks and precomputed sort/search keys.
    /// </summary>
    public (List<AlbumModel> Albums, List<ArtistModel> Artists) BuildGroupsFromTracks(IReadOnlyList<Track> tracks)
    {
        WarmupArtworkDirectoryCache();

        var albumList = tracks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) ? "Desconocido" : t.Album, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                string albumName = string.IsNullOrWhiteSpace(first.Album) ? "Desconocido" : first.Album;
                string artPath = !string.IsNullOrEmpty(first.ArtworkPath)
                    ? first.ArtworkPath
                    : Path.Combine(ArtworkCacheDir, $"{MetadataService.GetAlbumArtHash(first.Artist, albumName)}.jpg");

                bool hasArt = IsArtworkCached(artPath);
                var alphaKey = AlphaGroupHelper.GetAlphaGroupKey(albumName);
                var sortK = AlphaGroupHelper.GetSortKey(albumName);
                var rank = AlphaGroupHelper.GetGroupRank(alphaKey);

                DateTime latestDate = DateTime.MinValue;
                foreach (var tr in g)
                {
                    if (tr.DateAdded > latestDate) latestDate = tr.DateAdded;
                }

                var orderedTracks = g.OrderBy(t => t.TrackNumber).ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase).ToList();

                return new AlbumModel
                {
                    Name = albumName,
                    Artist = first.Artist,
                    Year = first.Year,
                    Genre = first.Genre,
                    ArtworkPath = hasArt ? artPath : null,
                    Tracks = orderedTracks,
                    PersistedTrackCount = orderedTracks.Count,
                    AlphaGroupKey = alphaKey,
                    SortKey = sortK,
                    GroupRank = rank,
                    LatestDateAdded = latestDate,
                    SearchText = AlphaGroupHelper.NormalizeForSearch($"{albumName} {first.Artist} {first.Genre}")
                };
            })
            .OrderBy(a => a.GroupRank)
            .ThenBy(a => a.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Build fast lookup for albums by name
        var albumByName = new Dictionary<string, AlbumModel>(albumList.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var al in albumList)
        {
            albumByName.TryAdd(al.Name, al);
        }

        // Build artists with smart splitter
        var artistTracksMap = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
        var artistCanonicalName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tracks)
        {
            var splitNames = ArtistSplitterService.Instance.SplitArtists(t.Artist);
            foreach (var name in splitNames)
            {
                if (!artistTracksMap.TryGetValue(name, out var list))
                {
                    list = new List<Track>();
                    artistTracksMap[name] = list;
                    artistCanonicalName[name] = name;
                }
                list.Add(t);
            }
        }

        var artistList = new List<ArtistModel>(artistTracksMap.Count);
        foreach (var kvp in artistTracksMap)
        {
            string artistName = artistCanonicalName[kvp.Key];
            var rawTracks = kvp.Value;

            // Deduplicate tracks by Id or FilePath while preserving order
            var uniqueTracks = new List<Track>(rawTracks.Count);
            var seenIds = new HashSet<long>();
            foreach (var tr in rawTracks)
            {
                if (tr.Id == 0 || seenIds.Add(tr.Id))
                {
                    uniqueTracks.Add(tr);
                }
            }

            var firstTrack = uniqueTracks[0];
            string artPath = !string.IsNullOrEmpty(firstTrack.ArtworkPath)
                ? firstTrack.ArtworkPath
                : Path.Combine(ArtworkCacheDir, $"{MetadataService.GetAlbumArtHash(firstTrack.Artist, firstTrack.Album)}.jpg");

            bool hasArt = IsArtworkCached(artPath);
            var alphaKey = AlphaGroupHelper.GetAlphaGroupKey(artistName);
            var sortK = AlphaGroupHelper.GetSortKey(artistName);
            var rank = AlphaGroupHelper.GetGroupRank(alphaKey);

            // Fast O(K) album association using dictionary instead of O(N*M) nested LINQ
            var artistAlbums = new List<AlbumModel>();
            var seenAlbums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tr in uniqueTracks)
            {
                string alName = string.IsNullOrWhiteSpace(tr.Album) ? "Desconocido" : tr.Album;
                if (seenAlbums.Add(alName) && albumByName.TryGetValue(alName, out var alModel))
                {
                    artistAlbums.Add(alModel);
                }
            }

            artistList.Add(new ArtistModel
            {
                Name = artistName,
                AvatarPath = hasArt ? artPath : null,
                Tracks = uniqueTracks,
                Albums = artistAlbums,
                PersistedTrackCount = uniqueTracks.Count,
                PersistedAlbumCount = artistAlbums.Count,
                AlphaGroupKey = alphaKey,
                SortKey = sortK,
                GroupRank = rank,
                SearchText = AlphaGroupHelper.NormalizeForSearch(artistName)
            });
        }

        artistList.Sort((a, b) =>
        {
            int cmp = a.GroupRank.CompareTo(b.GroupRank);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(a.SortKey, b.SortKey);
        });

        return (albumList, artistList);
    }

    /// <summary>
    /// Persists Songs, Albums, Artists, and Search FTS5 indexes in parallel across the 4 partitioned AppData databases.
    /// Called progressively during indexing and when library changes occur.
    /// </summary>
    public async Task SaveAllParallelAsync(
        IReadOnlyList<Track> tracks,
        IReadOnlyList<AlbumModel> albums,
        IReadOnlyList<ArtistModel> artists,
        bool replaceAll = true)
    {
        await _writeLock.WaitAsync();
        try
        {
            var tSongs = Task.Run(() => SaveSongsInternal(tracks, replaceAll));
            var tAlbums = Task.Run(() => SaveAlbumsInternal(albums, replaceAll));
            var tArtists = Task.Run(() => SaveArtistsInternal(artists, replaceAll));
            var tSearch = Task.Run(() => SaveSearchIndexInternal(tracks, albums, artists, replaceAll));

            await Task.WhenAll(tSongs, tAlbums, tArtists, tSearch);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LibraryIndexDatabase SaveAllParallelAsync error: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SaveSongsInternal(IReadOnlyList<Track> tracks, bool replaceAll)
    {
        using var conn = new SqliteConnection(_songsConnStr);
        conn.Open();
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        if (replaceAll)
        {
            cmd.CommandText = "DELETE FROM indexed_songs;";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = @"
            INSERT INTO indexed_songs (
                id, file_path, title, artist, album, year, track_number, genre,
                duration_ms, bitrate, sample_rate, bits_per_sample, channels, format,
                date_added_ticks, date_modified_ticks, file_size, is_favorite, play_count,
                artwork_path, first_letter_group, sort_title, group_rank, search_text
            ) VALUES (
                $id, $filePath, $title, $artist, $album, $year, $trackNum, $genre,
                $durMs, $bitrate, $sampleRate, $bits, $channels, $format,
                $addedTicks, $modTicks, $fileSize, $isFav, $playCount,
                $artPath, $firstGroup, $sortTitle, $groupRank, $searchText
            )
            ON CONFLICT(file_path) DO UPDATE SET
                id = excluded.id,
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
                date_added_ticks = excluded.date_added_ticks,
                date_modified_ticks = excluded.date_modified_ticks,
                file_size = excluded.file_size,
                is_favorite = excluded.is_favorite,
                play_count = excluded.play_count,
                artwork_path = excluded.artwork_path,
                first_letter_group = excluded.first_letter_group,
                sort_title = excluded.sort_title,
                group_rank = excluded.group_rank,
                search_text = excluded.search_text;
        ";

        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pPath = cmd.Parameters.Add("$filePath", SqliteType.Text);
        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pArtist = cmd.Parameters.Add("$artist", SqliteType.Text);
        var pAlbum = cmd.Parameters.Add("$album", SqliteType.Text);
        var pYear = cmd.Parameters.Add("$year", SqliteType.Integer);
        var pTrackNum = cmd.Parameters.Add("$trackNum", SqliteType.Integer);
        var pGenre = cmd.Parameters.Add("$genre", SqliteType.Text);
        var pDur = cmd.Parameters.Add("$durMs", SqliteType.Integer);
        var pBitrate = cmd.Parameters.Add("$bitrate", SqliteType.Integer);
        var pSampleRate = cmd.Parameters.Add("$sampleRate", SqliteType.Integer);
        var pBits = cmd.Parameters.Add("$bits", SqliteType.Integer);
        var pChannels = cmd.Parameters.Add("$channels", SqliteType.Integer);
        var pFormat = cmd.Parameters.Add("$format", SqliteType.Text);
        var pAdded = cmd.Parameters.Add("$addedTicks", SqliteType.Integer);
        var pMod = cmd.Parameters.Add("$modTicks", SqliteType.Integer);
        var pSize = cmd.Parameters.Add("$fileSize", SqliteType.Integer);
        var pFav = cmd.Parameters.Add("$isFav", SqliteType.Integer);
        var pPlay = cmd.Parameters.Add("$playCount", SqliteType.Integer);
        var pArt = cmd.Parameters.Add("$artPath", SqliteType.Text);
        var pFirst = cmd.Parameters.Add("$firstGroup", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sortTitle", SqliteType.Text);
        var pRank = cmd.Parameters.Add("$groupRank", SqliteType.Integer);
        var pSearch = cmd.Parameters.Add("$searchText", SqliteType.Text);

        foreach (var t in tracks)
        {
            pId.Value = t.Id;
            pPath.Value = t.FilePath;
            pTitle.Value = t.Title ?? "Unknown Title";
            pArtist.Value = t.Artist ?? "Unknown Artist";
            pAlbum.Value = t.Album ?? "Unknown Album";
            pYear.Value = (long)t.Year;
            pTrackNum.Value = (long)t.TrackNumber;
            pGenre.Value = t.Genre ?? "";
            pDur.Value = (long)t.Duration.TotalMilliseconds;
            pBitrate.Value = t.Bitrate;
            pSampleRate.Value = t.SampleRate;
            pBits.Value = t.BitsPerSample;
            pChannels.Value = t.Channels;
            pFormat.Value = t.FileFormat ?? "FLAC";
            pAdded.Value = t.DateAdded.Ticks;
            pMod.Value = t.DateModified.Ticks;
            pSize.Value = t.FileSize;
            pFav.Value = t.IsFavorite ? 1 : 0;
            pPlay.Value = t.PlayCount;
            pArt.Value = t.ArtworkPath ?? "";
            pFirst.Value = t.AlphaGroup;
            pSort.Value = t.SortTitle;
            pRank.Value = t.GroupRank;
            pSearch.Value = t.SearchText;
            cmd.ExecuteNonQuery();
        }

        trans.Commit();
    }

    private void SaveAlbumsInternal(IReadOnlyList<AlbumModel> albums, bool replaceAll)
    {
        using var conn = new SqliteConnection(_albumsConnStr);
        conn.Open();
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        if (replaceAll)
        {
            cmd.CommandText = "DELETE FROM indexed_albums;";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = @"
            INSERT INTO indexed_albums (
                name, artist, year, genre, artwork_path, alpha_group, sort_key,
                group_rank, latest_date_ticks, track_count, track_ids_csv, search_text
            ) VALUES (
                $name, $artist, $year, $genre, $artPath, $alpha, $sortKey,
                $rank, $latestTicks, $trackCount, $trackCsv, $searchText
            )
            ON CONFLICT(name) DO UPDATE SET
                artist = excluded.artist,
                year = excluded.year,
                genre = excluded.genre,
                artwork_path = excluded.artwork_path,
                alpha_group = excluded.alpha_group,
                sort_key = excluded.sort_key,
                group_rank = excluded.group_rank,
                latest_date_ticks = excluded.latest_date_ticks,
                track_count = excluded.track_count,
                track_ids_csv = excluded.track_ids_csv,
                search_text = excluded.search_text;
        ";

        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pArtist = cmd.Parameters.Add("$artist", SqliteType.Text);
        var pYear = cmd.Parameters.Add("$year", SqliteType.Integer);
        var pGenre = cmd.Parameters.Add("$genre", SqliteType.Text);
        var pArt = cmd.Parameters.Add("$artPath", SqliteType.Text);
        var pAlpha = cmd.Parameters.Add("$alpha", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sortKey", SqliteType.Text);
        var pRank = cmd.Parameters.Add("$rank", SqliteType.Integer);
        var pLatest = cmd.Parameters.Add("$latestTicks", SqliteType.Integer);
        var pCount = cmd.Parameters.Add("$trackCount", SqliteType.Integer);
        var pCsv = cmd.Parameters.Add("$trackCsv", SqliteType.Text);
        var pSearch = cmd.Parameters.Add("$searchText", SqliteType.Text);

        foreach (var a in albums)
        {
            pName.Value = a.Name;
            pArtist.Value = a.Artist ?? "";
            pYear.Value = (long)a.Year;
            pGenre.Value = a.Genre ?? "";
            pArt.Value = a.ArtworkPath ?? "";
            pAlpha.Value = a.AlphaGroup;
            pSort.Value = a.SortKey;
            pRank.Value = a.GroupRank;
            pLatest.Value = a.LatestDateAdded.Ticks;
            pCount.Value = a.TrackCount;
            pCsv.Value = string.Join(',', a.Tracks.Select(t => t.Id));
            pSearch.Value = a.SearchText;
            cmd.ExecuteNonQuery();
        }

        trans.Commit();
    }

    private void SaveArtistsInternal(IReadOnlyList<ArtistModel> artists, bool replaceAll)
    {
        using var conn = new SqliteConnection(_artistsConnStr);
        conn.Open();
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        if (replaceAll)
        {
            cmd.CommandText = "DELETE FROM indexed_artists;";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = @"
            INSERT INTO indexed_artists (
                name, avatar_path, alpha_group, sort_key, group_rank,
                track_count, album_count, track_ids_csv, album_names_csv, search_text
            ) VALUES (
                $name, $avatarPath, $alpha, $sortKey, $rank,
                $trackCount, $albumCount, $trackCsv, $albumCsv, $searchText
            )
            ON CONFLICT(name) DO UPDATE SET
                avatar_path = excluded.avatar_path,
                alpha_group = excluded.alpha_group,
                sort_key = excluded.sort_key,
                group_rank = excluded.group_rank,
                track_count = excluded.track_count,
                album_count = excluded.album_count,
                track_ids_csv = excluded.track_ids_csv,
                album_names_csv = excluded.album_names_csv,
                search_text = excluded.search_text;
        ";

        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pAvatar = cmd.Parameters.Add("$avatarPath", SqliteType.Text);
        var pAlpha = cmd.Parameters.Add("$alpha", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sortKey", SqliteType.Text);
        var pRank = cmd.Parameters.Add("$rank", SqliteType.Integer);
        var pTCount = cmd.Parameters.Add("$trackCount", SqliteType.Integer);
        var pACount = cmd.Parameters.Add("$albumCount", SqliteType.Integer);
        var pTCsv = cmd.Parameters.Add("$trackCsv", SqliteType.Text);
        var pACsv = cmd.Parameters.Add("$albumCsv", SqliteType.Text);
        var pSearch = cmd.Parameters.Add("$searchText", SqliteType.Text);

        foreach (var ar in artists)
        {
            pName.Value = ar.Name;
            pAvatar.Value = ar.AvatarPath ?? "";
            pAlpha.Value = ar.AlphaGroup;
            pSort.Value = ar.SortKey;
            pRank.Value = ar.GroupRank;
            pTCount.Value = ar.TrackCount;
            pACount.Value = ar.AlbumCount;
            pTCsv.Value = string.Join(',', ar.Tracks.Select(t => t.Id));
            pACsv.Value = string.Join('|', ar.Albums.Select(al => al.Name));
            pSearch.Value = ar.SearchText;
            cmd.ExecuteNonQuery();
        }

        trans.Commit();
    }

    private void SaveSearchIndexInternal(
        IReadOnlyList<Track> tracks,
        IReadOnlyList<AlbumModel> albums,
        IReadOnlyList<ArtistModel> artists,
        bool replaceAll)
    {
        using var conn = new SqliteConnection(_searchConnStr);
        conn.Open();
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        if (replaceAll)
        {
            cmd.CommandText = "DELETE FROM search_entries;";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = @"
            INSERT INTO search_entries (entity_type, entity_key, display_title, display_subtitle, search_text)
            VALUES ($type, $key, $title, $sub, $search)
            ON CONFLICT(entity_type, entity_key) DO UPDATE SET
                display_title = excluded.display_title,
                display_subtitle = excluded.display_subtitle,
                search_text = excluded.search_text;
        ";

        var pType = cmd.Parameters.Add("$type", SqliteType.Text);
        var pKey = cmd.Parameters.Add("$key", SqliteType.Text);
        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pSub = cmd.Parameters.Add("$sub", SqliteType.Text);
        var pSearch = cmd.Parameters.Add("$search", SqliteType.Text);

        foreach (var t in tracks)
        {
            pType.Value = "track";
            pKey.Value = t.Id > 0 ? t.Id.ToString() : t.FilePath;
            pTitle.Value = t.Title ?? "";
            pSub.Value = t.ArtistAndAlbum;
            pSearch.Value = t.SearchText;
            cmd.ExecuteNonQuery();
        }

        foreach (var al in albums)
        {
            pType.Value = "album";
            pKey.Value = al.Name;
            pTitle.Value = al.Name;
            pSub.Value = al.Subtitle;
            pSearch.Value = al.SearchText;
            cmd.ExecuteNonQuery();
        }

        foreach (var ar in artists)
        {
            pType.Value = "artist";
            pKey.Value = ar.Name;
            pTitle.Value = ar.Name;
            pSub.Value = ar.Subtitle;
            pSearch.Value = ar.SearchText;
            cmd.ExecuteNonQuery();
        }

        trans.Commit();
    }
}
