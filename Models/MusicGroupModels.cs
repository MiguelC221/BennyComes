using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class ArtistPhotoItem : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(FilePath);

    [ObservableProperty]
    private bool _isPrimary;

    [ObservableProperty]
    private bool _isOmitted;

    [ObservableProperty]
    private bool _isIncludedInRotation = true;

    partial void OnIsOmittedChanged(bool value)
    {
        IsIncludedInRotation = !value;
    }

    [ObservableProperty]
    private BitmapImage? _imagePreview;
}

public partial class AlbumModel : ObservableObject
{
    public string Name { get; set; } = "Unknown Album";
    public string Artist { get; set; } = "Unknown Artist";
    public uint Year { get; set; }
    public string Genre { get; set; } = string.Empty;
    public string? ArtworkPath { get; set; }
    public List<Track> Tracks { get; set; } = new();

    public int PersistedTrackCount { get; set; }
    public int TrackCount => Tracks.Count > 0 ? Tracks.Count : PersistedTrackCount;

    [ObservableProperty]
    private BitmapImage? _coverImage;

    [ObservableProperty]
    private bool _isFetchingMetadata;

    [ObservableProperty]
    private System.Windows.Media.Brush? _dominantHeaderBrush;

    partial void OnCoverImageChanged(BitmapImage? value)
    {
        if (value != null && DominantHeaderBrush == null)
        {
            DominantHeaderBrush = ExtractHeaderBrush(value);
        }
    }

    private bool _isLoadingCover;

    public BitmapImage? CoverImageSource
    {
        get
        {
            if (CoverImage != null)
            {
                if (DominantHeaderBrush == null)
                {
                    DominantHeaderBrush = ExtractHeaderBrush(CoverImage);
                }
                return CoverImage;
            }
            if (!_isLoadingCover)
            {
                _isLoadingCover = true;
                string? path = ArtworkPath;
                var fallbackTrack = Tracks.FirstOrDefault();

                Task.Run(() =>
                {
                    try
                    {
                        var img = LoadThumbnail(path, fallbackTrack);
                        if (img != null)
                        {
                            var brush = ExtractHeaderBrush(img);
                            App.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                CoverImage = img;
                                if (brush != null) DominantHeaderBrush = brush;
                                OnPropertyChanged(nameof(CoverImageSource));
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    catch { }
                    finally
                    {
                        _isLoadingCover = false;
                    }
                });
            }
            return null;
        }
    }

    public void ReleaseCoverFromMemory()
    {
        CoverImage = null;
        _isLoadingCover = false;
    }

    public string Subtitle => Year > 0 ? $"{Artist} • {Year}" : Artist;

    public string YearDisplay => Year > 0 ? Year.ToString() : "Año desconocido";

    public string TotalDurationFormatted
    {
        get
        {
            if (Tracks == null || Tracks.Count == 0) return "0:00";
            long totalTicks = 0;
            for (int i = 0; i < Tracks.Count; i++)
            {
                totalTicks += Tracks[i].Duration.Ticks;
            }
            var total = TimeSpan.FromTicks(totalTicks);
            return total.TotalHours >= 1
                ? $"{(int)total.TotalHours}:{total.Minutes:D2}:{total.Seconds:D2}"
                : $"{total.Minutes}:{total.Seconds:D2}";
        }
    }

    public string StatsSummary
    {
        get
        {
            int count = TrackCount;
            string songsText = count == 1 ? "1 canción" : $"{count} canciones";
            return $"{songsText} • Duración {TotalDurationFormatted}";
        }
    }

    public void RefreshStats()
    {
        if (Tracks != null)
        {
            for (int i = 0; i < Tracks.Count; i++)
            {
                Tracks[i].FallbackTrackIndex = i + 1;
            }
        }
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(TotalDurationFormatted));
        OnPropertyChanged(nameof(StatsSummary));
    }

    public string AlphaGroup => !string.IsNullOrWhiteSpace(AlphaGroupKey)
        ? AlphaGroupKey
        : Helpers.AlphaGroupHelper.GetAlphaGroupKey(Name);

    // Preloaded metadata for instantaneous sorting and filtering
    public string AlphaGroupKey { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
    public int GroupRank { get; set; } = 1;
    public DateTime LatestDateAdded { get; set; } = DateTime.MinValue;
    private string? _searchText;
    public string SearchText
    {
        get => _searchText ??= Helpers.AlphaGroupHelper.NormalizeForSearch($"{Name} {Artist} {Genre}");
        set => _searchText = value;
    }

    private static BitmapImage? LoadThumbnail(string? path, Track? fallbackTrack)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 220;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { }
        }

        return Services.Metadata.MetadataService.Instance.GetAlbumArtImage(fallbackTrack, decodeWidth: 220);
    }

    private static System.Windows.Media.Brush? ExtractHeaderBrush(BitmapSource? image)
    {
        if (image == null) return null;
        return Services.ThemeService.CreateAccentedBoxBrush(image);
    }
}

public partial class ArtistModel : ObservableObject
{
    public static bool IsLastFmArtistSyncActive { get; set; } = false;

    public string Name { get; set; } = "Unknown Artist";
    public List<string> AltNames { get; set; } = new();
    public bool HasAltNames => AltNames.Count > 0;
    public string AltNamesDisplay => AltNames.Count > 0 ? string.Join(" / ", AltNames) : string.Empty;
    public string? AvatarPath { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public List<AlbumModel> Albums { get; set; } = new();

    public int PersistedTrackCount { get; set; }
    public int PersistedAlbumCount { get; set; }

    public int TrackCount => Tracks.Count > 0 ? Tracks.Count : PersistedTrackCount;
    public int AlbumCount => Albums.Count > 0 ? Albums.Count : (Tracks.Count > 0 ? Tracks.Where(t => !string.IsNullOrWhiteSpace(t.Album)).Select(t => t.Album).Distinct().Count() : PersistedAlbumCount);

    // Preloaded metadata for instantaneous sorting and filtering
    public string AlphaGroupKey { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
    public int GroupRank { get; set; } = 1;
    private string? _searchText;
    public string SearchText
    {
        get => _searchText ??= Helpers.AlphaGroupHelper.NormalizeForSearch(
            AltNames.Count > 0 ? $"{Name} {string.Join(' ', AltNames)}" : Name);
        set => _searchText = value;
    }

    public bool MatchesQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        for (int i = 0; i < AltNames.Count; i++)
        {
            if (AltNames[i].Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    [ObservableProperty]
    private BitmapImage? _avatarImage;

    [ObservableProperty]
    private System.Windows.Media.Brush? _dominantHeaderBrush;

    partial void OnAvatarImageChanged(BitmapImage? value)
    {
        if (value != null)
        {
            DominantHeaderBrush = Services.ThemeService.CreateAccentedBoxBrush(value);
        }
    }

    private bool _isLoadingAvatar;

    [ObservableProperty]
    private string _biographySummary = string.Empty;

    [ObservableProperty]
    private string _biographyFull = string.Empty;

    [ObservableProperty]
    private bool _hasBiography;

    [ObservableProperty]
    private bool _hasCustomAvatar;

    [ObservableProperty]
    private bool _hasValidImage = true;

    public ObservableCollection<string> GalleryImages { get; } = new();

    public BitmapImage? AvatarImageSource
    {
        get
        {
            if (AvatarImage != null) return AvatarImage;
            if (!_isLoadingAvatar)
            {
                _isLoadingAvatar = true;
                string? path = AvatarPath;
                string artistName = Name;
                var fallbackTrack = Tracks.FirstOrDefault();

                Task.Run(() =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var (img, isCustom, hasImg) = LoadThumbnailHierarchy(path, artistName, fallbackTrack);
                        sw.Stop();
                        if (sw.Elapsed.TotalMilliseconds > 5)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PERF] ArtistModel.LoadThumbnailHierarchy('{artistName}'): {sw.Elapsed.TotalMilliseconds:F1} ms");
                        }
                        App.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            HasCustomAvatar = isCustom;
                            HasValidImage = hasImg;
                            AvatarImage = img;
                            if (img != null)
                            {
                                DominantHeaderBrush = Services.ThemeService.CreateAccentedBoxBrush(img);
                            }
                            OnPropertyChanged(nameof(AvatarImageSource));
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                    catch { }
                    finally
                    {
                        _isLoadingAvatar = false;
                    }
                });
            }
            return null;
        }
    }

    public string Subtitle => AlbumCount > 0 ? $"{AlbumCount} álbumes • {TrackCount} canciones" : $"{TrackCount} canciones";

    public string PrimaryStatsText
    {
        get
        {
            int albums = AlbumCount;
            int tracks = TrackCount;
            string albumsWord = albums == 1 ? "1 álbum" : $"{albums} álbumes";
            string tracksWord = tracks == 1 ? "1 canción" : $"{tracks} canciones";
            return albums > 0 ? $"{albumsWord} • {tracksWord}" : tracksWord;
        }
    }

    public string DurationStatsText
    {
        get
        {
            if (Tracks == null || Tracks.Count == 0) return string.Empty;
            long totalTicks = 0;
            for (int i = 0; i < Tracks.Count; i++)
            {
                totalTicks += Tracks[i].Duration.Ticks;
            }
            var total = TimeSpan.FromTicks(totalTicks);
            if (total.TotalHours >= 1)
            {
                int hrs = (int)total.TotalHours;
                string hrWord = hrs == 1 ? "1 hora" : $"{hrs} horas";
                return total.Minutes > 0
                    ? $" • {hrWord} {total.Minutes} min"
                    : $" • {hrWord}";
            }
            if (total.Minutes >= 1)
            {
                return $" • {total.Minutes} min";
            }
            return $" • {Math.Max(1, total.Seconds)} s";
        }
    }

    public string MainGenreDisplay
    {
        get
        {
            if (Tracks != null && Tracks.Count > 0)
            {
                var topGenre = Tracks
                    .Select(t => t.Genre?.Trim())
                    .Where(g => !string.IsNullOrWhiteSpace(g) &&
                                !g.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                                !g.Equals("Desconocido", StringComparison.OrdinalIgnoreCase) &&
                                !g.Equals("Género desconocido", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(g => g!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(grp => grp.Count())
                    .Select(grp => grp.First())
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(topGenre))
                {
                    return $"Artista • {topGenre}";
                }
            }
            return "Artista • Género desconocido";
        }
    }

    public void EnsureAlbumsAndStats()
    {
        if (Tracks.Count == 0)
        {
            Tracks = Services.Library.LibraryCacheService.Instance.CachedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Artist) &&
                            t.Artist.Contains(Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var albumGroups = Tracks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) || t.Album.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(t.Title) ? "Single" : t.Title.Trim())
                : t.Album.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var orderedTracks = g
                    .OrderBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < orderedTracks.Count; i++)
                {
                    orderedTracks[i].FallbackTrackIndex = i + 1;
                }

                var first = orderedTracks[0];
                var existingAlbum = Services.Library.LibraryCacheService.Instance.CachedAlbums.FirstOrDefault(a =>
                    string.Equals(a.Name, g.Key, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(a.Artist, Name, StringComparison.OrdinalIgnoreCase) || a.Artist.Contains(Name, StringComparison.OrdinalIgnoreCase)));

                return new AlbumModel
                {
                    Name = g.Key,
                    Artist = !string.IsNullOrWhiteSpace(first.AlbumArtist) ? first.AlbumArtist : Name,
                    Year = orderedTracks.Max(t => t.Year),
                    Genre = first.Genre,
                    ArtworkPath = existingAlbum?.ArtworkPath ?? first.ArtworkPath,
                    Tracks = orderedTracks,
                    PersistedTrackCount = orderedTracks.Count
                };
            })
            .OrderBy(a => a.Year > 0 ? 0 : 1)
            .ThenByDescending(a => a.Year)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Albums = albumGroups;
        OnPropertyChanged(nameof(Albums));
        OnPropertyChanged(nameof(AlbumCount));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(PrimaryStatsText));
        OnPropertyChanged(nameof(DurationStatsText));
        OnPropertyChanged(nameof(MainGenreDisplay));
    }

    public string AlphaGroup => !string.IsNullOrWhiteSpace(AlphaGroupKey)
        ? AlphaGroupKey
        : Helpers.AlphaGroupHelper.GetAlphaGroupKey(Name);

    public void RefreshAvatar(string? newPath = null)
    {
        if (!string.IsNullOrEmpty(newPath))
        {
            AvatarPath = newPath;
        }
        AvatarImage = null;
        _isLoadingAvatar = false;
        OnPropertyChanged(nameof(AvatarImageSource));
        OnPropertyChanged(nameof(HasCustomAvatar));
        OnPropertyChanged(nameof(HasValidImage));
    }

    public void ReleaseAvatarFromMemory()
    {
        AvatarImage = null;
        _isLoadingAvatar = false;
        foreach (var al in Albums)
        {
            al.ReleaseCoverFromMemory();
        }
    }

    private static (BitmapImage? Image, bool IsCustom, bool HasImg) LoadThumbnailHierarchy(string? path, string artistName, Track? fallbackTrack)
    {
        // 1. Check specified AvatarPath
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var loaded = TryLoadBitmap(path);
            if (loaded != null) return (loaded, true, true);
        }

        // 1.b Check ArtistCache directory for Last.fm / local downloaded photos
        try
        {
            string folder = Services.Online.LastFmService.SanitizeFolderName(artistName);
            string artistDir = Path.Combine(Services.Online.LastFmService.ArtistCacheBaseDirectory, folder);
            if (Directory.Exists(artistDir))
            {
                var localImages = Services.Online.LastFmService.Instance.GetLocalArtistImages(artistDir);
                if (localImages.Count > 0 && File.Exists(localImages[0]))
                {
                    var loaded = TryLoadBitmap(localImages[0]);
                    if (loaded != null) return (loaded, true, true);
                }
            }
        }
        catch { }

        // If Last.fm artist search/sync is active AND Last.fm service is enabled, keep image blank (silhouette) until downloaded
        if (IsLastFmArtistSyncActive && Services.Online.LastFmService.Instance.IsEnabled)
        {
            return (null, false, false);
        }

        // 2. Fallback to album art from one of their local tracks
        fallbackTrack ??= Services.Library.LibraryCacheService.Instance.CachedTracks
            .FirstOrDefault(t => !string.IsNullOrEmpty(t.Artist) && t.Artist.Contains(artistName, StringComparison.OrdinalIgnoreCase));

        var albumArt = Services.Metadata.MetadataService.Instance.GetAlbumArtImage(fallbackTrack, decodeWidth: 160);
        if (albumArt != null)
        {
            return (albumArt, false, true);
        }

        // 3. Silhouette generic fallback (return null so UI shows silhouette)
        return (null, false, false);
    }

    private static BitmapImage? TryLoadBitmap(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 180;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}

public class ArtistLetterSection
{
    public string Letter { get; set; } = string.Empty;
    public bool HasLetter => !string.IsNullOrEmpty(Letter);
    public List<ArtistModel> Artists { get; set; } = new();

    public static readonly string[] SectionLetters = new[]
    {
        "#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"
    };

    public static string GetSectionLetter(string? alphaGroup)
    {
        if (!string.IsNullOrEmpty(alphaGroup) && alphaGroup.Length == 1)
        {
            char c = char.ToUpperInvariant(alphaGroup[0]);
            if (c >= 'A' && c <= 'Z') return c.ToString();
        }
        return "#";
    }

    public static List<ArtistLetterSection> BuildSections(IEnumerable<ArtistModel> artists, bool reverseOrder = false)
    {
        var buckets = new Dictionary<string, List<ArtistModel>>(27, StringComparer.OrdinalIgnoreCase);
        foreach (var letter in SectionLetters)
        {
            buckets[letter] = new List<ArtistModel>();
        }

        foreach (var artist in artists)
        {
            string key = GetSectionLetter(artist.AlphaGroup);
            buckets[key].Add(artist);
        }

        var orderedKeys = reverseOrder ? SectionLetters.Reverse() : SectionLetters;
        var sections = new List<ArtistLetterSection>(27);
        foreach (var letter in orderedKeys)
        {
            var list = buckets[letter];
            if (list.Count > 0)
            {
                sections.Add(new ArtistLetterSection
                {
                    Letter = letter,
                    Artists = list
                });
            }
        }

        return sections;
    }
}

public class DoubtfulArtistPair
{
    public string UnicodeName { get; set; } = string.Empty;
    public string RomajiName { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DisplayPair => $"{RomajiName}  ↔  {UnicodeName}";
    public string Subtitle => $"{TrackCount} canciones • {Reason}";
}


