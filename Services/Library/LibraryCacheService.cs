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
        await ReloadFromDatabaseAsync();
    }

    public async Task ReloadFromDatabaseAsync()
    {
        try
        {
            var tracks = await _libraryDao.GetAllTracksAsync();
            var genres = await _libraryDao.GetGenresAsync();

            var (albums, artists) = await Task.Run(() =>
            {
                // Build albums
                var albumList = tracks
                    .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) ? "Desconocido" : t.Album)
                    .Select(g =>
                    {
                        var first = g.First();
                        string artHash = MetadataService.GetAlbumArtHash(first.Artist, g.Key);
                        string artPath = Path.Combine(ArtworkCacheDir, $"{artHash}.jpg");

                        return new AlbumModel
                        {
                            Name = g.Key,
                            Artist = first.Artist,
                            Year = first.Year,
                            Genre = first.Genre,
                            ArtworkPath = File.Exists(artPath) ? artPath : null,
                            Tracks = g.OrderBy(t => t.TrackNumber).ToList()
                        };
                    })
                    .OrderBy(a => a.Name)
                    .ToList();

                // Build artists with smart splitter
                var artistTrackPairs = new List<(string ArtistName, Track Track)>();
                foreach (var t in tracks)
                {
                    var splitNames = ArtistSplitterService.Instance.SplitArtists(t.Artist);
                    foreach (var name in splitNames)
                    {
                        artistTrackPairs.Add((name, t));
                    }
                }

                var artistList = artistTrackPairs
                    .GroupBy(pair => pair.ArtistName, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var firstTrack = g.First().Track;
                        string artHash = MetadataService.GetAlbumArtHash(firstTrack.Artist, firstTrack.Album);
                        string artPath = Path.Combine(ArtworkCacheDir, $"{artHash}.jpg");

                        var artistAlbums = albumList
                            .Where(al => al.Artist.Equals(g.Key, StringComparison.OrdinalIgnoreCase) ||
                                         g.Any(pair => string.Equals(pair.Track.Album, al.Name, StringComparison.OrdinalIgnoreCase)))
                            .DistinctBy(al => al.Name)
                            .ToList();

                        return new ArtistModel
                        {
                            Name = g.Key,
                            AvatarPath = File.Exists(artPath) ? artPath : null,
                            Tracks = g.Select(p => p.Track).DistinctBy(t => t.Id).ToList(),
                            Albums = artistAlbums
                        };
                    })
                    .OrderBy(a => a.Name)
                    .ToList();

                return (albumList, artistList);
            });

            // Save grouped caches to SQLite asynchronously for durability
            _ = Task.Run(async () =>
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
            });

            CachedTracks = tracks;
            CachedAlbums = albums;
            CachedArtists = artists;
            CachedGenres = genres;
            IsLoaded = true;

            await LoadRecentPlaysFromDbAsync();

            LibraryCacheUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading library cache: {ex.Message}");
        }
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
        IEnumerable<Track> query = CachedTracks;

        if (!string.IsNullOrWhiteSpace(genre) && !genre.Equals("Todos los géneros", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => !string.IsNullOrEmpty(t.Genre) && t.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(t =>
                (t.Title?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Artist?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Album?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var sorted = sortKey switch
        {
            "Álbum" or "Album" => isDescending
                ? query.OrderByDescending(t => t.Album).ThenByDescending(t => t.TrackNumber)
                : query.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber),
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
                ? query.OrderByDescending(t => t.Genre).ThenByDescending(t => t.Title)
                : query.OrderBy(t => t.Genre).ThenBy(t => t.Title),
            _ => isDescending
                ? query.OrderByDescending(t => t.Title)
                : query.OrderBy(t => t.Title)
        };

        return sorted.ToList();
    }

    public List<AlbumModel> GetFilteredAlbums(string sortKey, string genre, string search, bool isDescending = false)
    {
        IEnumerable<AlbumModel> query = CachedAlbums;

        if (!string.IsNullOrWhiteSpace(genre) && !genre.Equals("Todos los géneros", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => !string.IsNullOrEmpty(a.Genre) && a.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(a =>
                (a.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Artist?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var sorted = sortKey switch
        {
            "Nombre (A-Z)" => isDescending
                ? query.OrderByDescending(a => a.Name)
                : query.OrderBy(a => a.Name),
            "Artista" => isDescending
                ? query.OrderByDescending(a => a.Artist).ThenByDescending(a => a.Name)
                : query.OrderBy(a => a.Artist).ThenBy(a => a.Name),
            "Año" => isDescending
                ? query.OrderBy(a => a.Year)
                : query.OrderByDescending(a => a.Year),
            _ => isDescending
                ? query.OrderBy(a => a.Tracks.Count > 0 ? a.Tracks.Max(t => t.DateAdded) : DateTime.MinValue)
                : query.OrderByDescending(a => a.Tracks.Count > 0 ? a.Tracks.Max(t => t.DateAdded) : DateTime.MinValue)
        };

        return sorted.ToList();
    }

    public List<ArtistModel> GetFilteredArtists(string sortKey, string search, bool isDescending = false)
    {
        IEnumerable<ArtistModel> query = CachedArtists;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(a => a.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
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
                ? query.OrderByDescending(a => a.Name)
                : query.OrderBy(a => a.Name)
        };

        return sorted.ToList();
    }
}
