using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Metadata;
using Microsoft.Data.Sqlite;

namespace BenyComes.Services.Library;

public class LibraryCacheService
{
    private static readonly Lazy<LibraryCacheService> _lazy = new(() => new LibraryCacheService());
    public static LibraryCacheService Instance => _lazy.Value;

    private readonly LibraryDao _libraryDao = LibraryDao.Instance;
    private readonly DatabaseContext _db = DatabaseContext.Instance;

    private static readonly string ArtworkCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer",
        "ArtworkCache"
    );

    public List<Track> CachedTracks { get; private set; } = new();
    public List<AlbumModel> CachedAlbums { get; private set; } = new();
    public List<ArtistModel> CachedArtists { get; private set; } = new();
    public List<string> CachedGenres { get; private set; } = new();

    public ObservableCollection<Track> RecentTracks { get; } = new();
    public ObservableCollection<AlbumModel> RecentAlbums { get; } = new();

    public bool IsLoaded { get; private set; } = false;

    public event Action? LibraryCacheUpdated;
    public event Action? RecentPlaysUpdated;

    private LibraryCacheService() { }

    public async Task InitializeAsync()
    {
        if (IsLoaded) return;
        await ArtistSplitterService.Instance.LoadSettingsAsync();

        // Fast-path: load precomputed Songs, Albums, and Artists concurrently from %LocalAppData%\BenyComes\IndexDB\
        var (hasData, indexedTracks, indexedAlbums, indexedArtists, indexedGenres) =
            await LibraryIndexDatabase.Instance.TryLoadAllParallelAsync();

        if (hasData && indexedTracks.Count > 0)
        {
            CachedTracks = indexedTracks;
            CachedAlbums = indexedAlbums;
            CachedArtists = indexedArtists;
            CachedGenres = indexedGenres;
            IsLoaded = true;

            _ = LoadRecentPlaysFromDbAsync();
            LibraryCacheUpdated?.Invoke();
            return;
        }

        await ReloadFromDatabaseAsync();
    }

    public async Task ReloadFromDatabaseAsync()
    {
        try
        {
            var tracks = await _libraryDao.GetAllTracksAsync();
            var genres = await _libraryDao.GetGenresAsync();

            var (albums, artists) = await Task.Run(() =>
                LibraryIndexDatabase.Instance.BuildGroupsFromTracks(tracks));

            CachedTracks = tracks;
            CachedAlbums = albums;
            CachedArtists = artists;
            CachedGenres = genres;
            IsLoaded = true;

            // Persist partitioned index databases in %LocalAppData%\BenyComes\IndexDB\ + legacy cache tables
            _ = Task.Run(async () =>
            {
                await LibraryIndexDatabase.Instance.SaveAllParallelAsync(tracks, albums, artists, replaceAll: true);
                await SaveLegacyGroupedCachesAsync(albums, artists);
            });

            await LoadRecentPlaysFromDbAsync();
            NotifyLibraryCacheUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading library cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Integrates a newly indexed batch of tracks into memory and flushes progressively to the partitioned AppData
    /// index databases (songs_index.db, albums_index.db, artists_index.db, search_index.db) while indexing is running.
    /// </summary>
    public async Task IntegrateIndexedBatchAsync(IReadOnlyList<Track> batchTracks, bool isFinalBatch)
    {
        if (batchTracks == null || batchTracks.Count == 0) return;

        try
        {
            var (mergedTracks, albums, artists, genres) = await Task.Run(() =>
            {
                var trackMap = new Dictionary<string, Track>(CachedTracks.Count + batchTracks.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var existing in CachedTracks)
                {
                    if (!string.IsNullOrEmpty(existing.FilePath))
                        trackMap[existing.FilePath] = existing;
                }

                foreach (var incoming in batchTracks)
                {
                    if (!string.IsNullOrEmpty(incoming.FilePath))
                        trackMap[incoming.FilePath] = incoming;
                }

                var allTracks = trackMap.Values
                    .OrderBy(t => t.GroupRank)
                    .ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var (builtAlbums, builtArtists) = LibraryIndexDatabase.Instance.BuildGroupsFromTracks(allTracks);

                var genreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in allTracks)
                {
                    if (!string.IsNullOrWhiteSpace(t.Genre))
                        genreSet.Add(t.Genre);
                }

                var builtGenres = new List<string>(genreSet.Count + 1) { "Todos los géneros" };
                builtGenres.AddRange(genreSet.OrderBy(g => g, StringComparer.OrdinalIgnoreCase));

                return (allTracks, builtAlbums, builtArtists, builtGenres);
            });

            CachedTracks = mergedTracks;
            CachedAlbums = albums;
            CachedArtists = artists;
            CachedGenres = genres;
            IsLoaded = true;

            // Flush batch progressively to %LocalAppData%\BenyComes\IndexDB\ across all 4 databases in parallel
            var tracksToSave = isFinalBatch ? mergedTracks : batchTracks;
            await LibraryIndexDatabase.Instance.SaveAllParallelAsync(tracksToSave, albums, artists, replaceAll: isFinalBatch);

            if (isFinalBatch)
            {
                _ = Task.Run(() => SaveLegacyGroupedCachesAsync(albums, artists));
                await LoadRecentPlaysFromDbAsync();
            }

            NotifyLibraryCacheUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error integrating indexed batch: {ex.Message}");
        }
    }

    private void NotifyLibraryCacheUpdated()
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.InvokeAsync(() => LibraryCacheUpdated?.Invoke());
        }
        else
        {
            LibraryCacheUpdated?.Invoke();
        }
    }

    private async Task SaveLegacyGroupedCachesAsync(IReadOnlyList<AlbumModel> albums, IReadOnlyList<ArtistModel> artists)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            using var trans = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;

            cmd.CommandText = @"
                INSERT INTO cached_albums (name, artist, year, genre, track_count, artwork_path)
                VALUES ($name, $artist, $year, $genre, $trackCount, $artworkPath)
                ON CONFLICT(name, artist) DO UPDATE SET
                    year = excluded.year,
                    genre = excluded.genre,
                    track_count = excluded.track_count,
                    artwork_path = excluded.artwork_path;
            ";

            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pArtist = cmd.Parameters.Add("$artist", SqliteType.Text);
            var pYear = cmd.Parameters.Add("$year", SqliteType.Integer);
            var pGenre = cmd.Parameters.Add("$genre", SqliteType.Text);
            var pCount = cmd.Parameters.Add("$trackCount", SqliteType.Integer);
            var pArt = cmd.Parameters.Add("$artworkPath", SqliteType.Text);

            foreach (var a in albums)
            {
                pName.Value = a.Name;
                pArtist.Value = a.Artist;
                pYear.Value = (long)a.Year;
                pGenre.Value = a.Genre;
                pCount.Value = a.TrackCount;
                pArt.Value = a.ArtworkPath ?? "";
                await cmd.ExecuteNonQueryAsync();
            }

            cmd.CommandText = @"
                INSERT INTO cached_artists (name, track_count, album_count, avatar_path)
                VALUES ($name, $trackCount, $albumCount, $avatarPath)
                ON CONFLICT(name) DO UPDATE SET
                    track_count = excluded.track_count,
                    album_count = excluded.album_count,
                    avatar_path = excluded.avatar_path;
            ";

            cmd.Parameters.Clear();
            var paName = cmd.Parameters.Add("$name", SqliteType.Text);
            var paTrackCount = cmd.Parameters.Add("$trackCount", SqliteType.Integer);
            var paAlbumCount = cmd.Parameters.Add("$albumCount", SqliteType.Integer);
            var paAvatar = cmd.Parameters.Add("$avatarPath", SqliteType.Text);

            foreach (var ar in artists)
            {
                paName.Value = ar.Name;
                paTrackCount.Value = ar.TrackCount;
                paAlbumCount.Value = ar.AlbumCount;
                paAvatar.Value = ar.AvatarPath ?? "";
                await cmd.ExecuteNonQueryAsync();
            }

            await trans.CommitAsync();
        }
        catch { }
    }

    public async Task LoadRecentPlaysFromDbAsync()
    {
        try
        {
            var tracks = await _libraryDao.GetRecentHistoryAsync(30);
            var app = Application.Current;
            Action update = () =>
            {
                RecentTracks.Clear();
                foreach (var t in tracks.Take(30))
                {
                    RecentTracks.Add(t);
                }

                var albumList = tracks
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) ? "Desconocido" : t.Album)
                    .Select(g =>
                    {
                        var first = g.First();
                        var cachedAlbum = CachedAlbums.FirstOrDefault(a => a.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                        return cachedAlbum ?? new AlbumModel
                        {
                            Name = g.Key,
                            Artist = first.Artist,
                            Year = first.Year,
                            Tracks = g.ToList(),
                            CoverImage = MetadataService.Instance.GetAlbumArtImage(first)
                        };
                    })
                    .Take(15)
                    .ToList();

                RecentAlbums.Clear();
                foreach (var a in albumList)
                {
                    RecentAlbums.Add(a);
                }

                RecentPlaysUpdated?.Invoke();
            };

            if (app != null)
            {
                if (app.Dispatcher.CheckAccess()) update();
                else app.Dispatcher.Invoke(update);
            }
            else
            {
                update();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading recent plays: {ex.Message}");
        }
    }

    public async Task RecordRecentTrackAsync(Track track)
    {
        if (track == null) return;

        var app = Application.Current;
        Action updateUi = () =>
        {
            var existing = RecentTracks.FirstOrDefault(t => t.Id == track.Id || (!string.IsNullOrEmpty(t.FilePath) && t.FilePath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)));
            if (existing != null)
            {
                RecentTracks.Remove(existing);
            }
            RecentTracks.Insert(0, track);
            while (RecentTracks.Count > 30)
            {
                RecentTracks.RemoveAt(RecentTracks.Count - 1);
            }

            string albumName = string.IsNullOrWhiteSpace(track.Album) ? "Desconocido" : track.Album;
            var existingAlbum = RecentAlbums.FirstOrDefault(a => a.Name.Equals(albumName, StringComparison.OrdinalIgnoreCase));
            if (existingAlbum != null)
            {
                RecentAlbums.Remove(existingAlbum);
                RecentAlbums.Insert(0, existingAlbum);
            }
            else
            {
                var cachedAlbum = CachedAlbums.FirstOrDefault(a => a.Name.Equals(albumName, StringComparison.OrdinalIgnoreCase));
                var albumModel = cachedAlbum ?? new AlbumModel
                {
                    Name = albumName,
                    Artist = track.Artist,
                    Year = track.Year,
                    CoverImage = MetadataService.Instance.GetAlbumArtImage(track)
                };
                if (albumModel.Tracks.Count == 0)
                {
                    albumModel.Tracks = CachedTracks.Where(t => t.Album.Equals(albumName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (albumModel.Tracks.Count == 0) albumModel.Tracks.Add(track);
                }
                RecentAlbums.Insert(0, albumModel);
            }
            while (RecentAlbums.Count > 15)
            {
                RecentAlbums.RemoveAt(RecentAlbums.Count - 1);
            }

            RecentPlaysUpdated?.Invoke();
        };

        if (app != null)
        {
            if (app.Dispatcher.CheckAccess()) updateUi();
            else app.Dispatcher.Invoke(updateUi);
        }
        else
        {
            updateUi();
        }

        _ = _libraryDao.RecordRecentPlayAsync(track);
        await Task.CompletedTask;
    }

    public List<Track> GetFilteredTracks(string sortKey, string genre, string search, bool isDescending = false)
    {
        bool hasGenreFilter = !string.IsNullOrWhiteSpace(genre) && !genre.Equals("Todos los géneros", StringComparison.OrdinalIgnoreCase);
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        bool isDefaultSort = sortKey is not ("Álbum" or "Album" or "Duración" or "Duration" or "Fecha modificada" or "DateModified" or "Fecha de adición" or "DateAdded" or "Género musical" or "Genre");

        // Zero-sort fast path: CachedTracks is already ordered by (GroupRank ASC, SortTitle ASC)
        if (!hasGenreFilter && !hasSearch && isDefaultSort && !isDescending)
        {
            return new List<Track>(CachedTracks);
        }

        IEnumerable<Track> query = CachedTracks;

        if (hasGenreFilter)
        {
            query = query.Where(t => !string.IsNullOrEmpty(t.Genre) && t.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        }

        if (hasSearch)
        {
            string normSearch = Helpers.AlphaGroupHelper.NormalizeForSearch(search);
            var terms = normSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 1)
            {
                string term = terms[0];
                query = query.Where(t => t.SearchText.Contains(term, StringComparison.Ordinal));
            }
            else if (terms.Length > 1)
            {
                query = query.Where(t =>
                {
                    var st = t.SearchText;
                    for (int i = 0; i < terms.Length; i++)
                    {
                        if (!st.Contains(terms[i], StringComparison.Ordinal)) return false;
                    }
                    return true;
                });
            }
        }

        if (isDefaultSort && !isDescending)
        {
            return query.ToList();
        }

        var sorted = sortKey switch
        {
            "Álbum" or "Album" => isDescending
                ? query.OrderByDescending(t => t.Album, StringComparer.OrdinalIgnoreCase).ThenByDescending(t => t.TrackNumber)
                : query.OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TrackNumber),
            "Duración" or "Duration" => isDescending
                ? query.OrderBy(t => t.Duration)
                : query.OrderByDescending(t => t.Duration),
            "Fecha modificada" or "DateModified" => isDescending
                ? query.OrderBy(t => t.DateModified)
                : query.OrderByDescending(t => t.DateModified),
            "Fecha de adición" or "DateAdded" => isDescending
                ? query.OrderBy(t => t.DateAdded)
                : query.OrderByDescending(t => t.DateAdded),
            "Género musical" or "Genre" => isDescending
                ? query.OrderByDescending(t => t.Genre, StringComparer.OrdinalIgnoreCase).ThenByDescending(t => t.GroupRank).ThenByDescending(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(t => t.Genre, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.GroupRank).ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase),
            _ => isDescending
                ? query.OrderByDescending(t => t.GroupRank).ThenByDescending(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(t => t.GroupRank).ThenBy(t => t.SortTitle, StringComparer.OrdinalIgnoreCase)
        };

        return sorted.ToList();
    }

    public List<AlbumModel> GetFilteredAlbums(string sortKey, string genre, string search, bool isDescending = false)
    {
        bool hasGenreFilter = !string.IsNullOrWhiteSpace(genre) && !genre.Equals("Todos los géneros", StringComparison.OrdinalIgnoreCase);
        bool hasSearch = !string.IsNullOrWhiteSpace(search);

        // Zero-sort fast path when ordered by Nombre (A-Z) ascending
        if (!hasGenreFilter && !hasSearch && sortKey == "Nombre (A-Z)" && !isDescending)
        {
            return new List<AlbumModel>(CachedAlbums);
        }

        IEnumerable<AlbumModel> query = CachedAlbums;

        if (hasGenreFilter)
        {
            query = query.Where(a => !string.IsNullOrEmpty(a.Genre) && a.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        }

        if (hasSearch)
        {
            string normSearch = Helpers.AlphaGroupHelper.NormalizeForSearch(search);
            var terms = normSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 1)
            {
                string term = terms[0];
                query = query.Where(a => a.SearchText.Contains(term, StringComparison.Ordinal));
            }
            else if (terms.Length > 1)
            {
                query = query.Where(a =>
                {
                    var st = a.SearchText;
                    for (int i = 0; i < terms.Length; i++)
                    {
                        if (!st.Contains(terms[i], StringComparison.Ordinal)) return false;
                    }
                    return true;
                });
            }
        }

        var sorted = sortKey switch
        {
            "Nombre (A-Z)" => isDescending
                ? query.OrderByDescending(a => a.GroupRank).ThenByDescending(a => a.SortKey, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(a => a.GroupRank).ThenBy(a => a.SortKey, StringComparer.OrdinalIgnoreCase),
            "Artista" => isDescending
                ? query.OrderByDescending(a => a.Artist, StringComparer.OrdinalIgnoreCase).ThenByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(a => a.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            "Año" => isDescending
                ? query.OrderBy(a => a.Year)
                : query.OrderByDescending(a => a.Year),
            _ => isDescending
                ? query.OrderBy(a => a.LatestDateAdded)
                : query.OrderByDescending(a => a.LatestDateAdded)
        };

        return sorted.ToList();
    }

    public List<ArtistModel> GetFilteredArtists(string sortKey, string search, bool isDescending = false)
    {
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        bool isDefaultSort = sortKey is not ("Cantidad de canciones" or "Cantidad de álbumes");

        // Zero-sort fast path: CachedArtists is already ordered by (GroupRank ASC, SortKey ASC)
        if (!hasSearch && isDefaultSort && !isDescending)
        {
            return new List<ArtistModel>(CachedArtists);
        }

        IEnumerable<ArtistModel> query = CachedArtists;

        if (hasSearch)
        {
            string normSearch = Helpers.AlphaGroupHelper.NormalizeForSearch(search);
            var terms = normSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 1)
            {
                string term = terms[0];
                query = query.Where(a => a.SearchText.Contains(term, StringComparison.Ordinal));
            }
            else if (terms.Length > 1)
            {
                query = query.Where(a =>
                {
                    var st = a.SearchText;
                    for (int i = 0; i < terms.Length; i++)
                    {
                        if (!st.Contains(terms[i], StringComparison.Ordinal)) return false;
                    }
                    return true;
                });
            }
        }

        if (isDefaultSort && !isDescending)
        {
            return query.ToList();
        }

        var sorted = sortKey switch
        {
            "Cantidad de canciones" => isDescending
                ? query.OrderBy(a => a.TrackCount)
                : query.OrderByDescending(a => a.TrackCount),
            "Cantidad de álbumes" => isDescending
                ? query.OrderBy(a => a.AlbumCount)
                : query.OrderByDescending(a => a.AlbumCount),
            _ => isDescending
                ? query.OrderByDescending(a => a.GroupRank).ThenByDescending(a => a.SortKey, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(a => a.GroupRank).ThenBy(a => a.SortKey, StringComparer.OrdinalIgnoreCase)
        };

        return sorted.ToList();
    }
}
