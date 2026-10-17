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

    private bool _isLoadingCover;

    public BitmapImage? CoverImageSource
    {
        get
        {
            if (CoverImage != null) return CoverImage;
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
                            App.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                CoverImage = img;
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
                bi.DecodePixelWidth = 160;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { }
        }

        return Services.Metadata.MetadataService.Instance.GetAlbumArtImage(fallbackTrack, decodeWidth: 160);
    }
}

public partial class ArtistModel : ObservableObject
{
    public static bool IsLastFmArtistSyncActive { get; set; } = false;

    public string Name { get; set; } = "Unknown Artist";
    public string? AvatarPath { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public List<AlbumModel> Albums { get; set; } = new();

    public int PersistedTrackCount { get; set; }
    public int PersistedAlbumCount { get; set; }

    public int TrackCount => Tracks.Count > 0 ? Tracks.Count : PersistedTrackCount;
    public int AlbumCount => Albums.Count > 0 ? Albums.Count : (Tracks.Count > 0 ? Tracks.Select(t => t.Album).Distinct().Count() : PersistedAlbumCount);

    // Preloaded metadata for instantaneous sorting and filtering
    public string AlphaGroupKey { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
    public int GroupRank { get; set; } = 1;
    private string? _searchText;
    public string SearchText
    {
        get => _searchText ??= Helpers.AlphaGroupHelper.NormalizeForSearch(Name);
        set => _searchText = value;
    }

    [ObservableProperty]
    private BitmapImage? _avatarImage;

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
                        var (img, isCustom, hasImg) = LoadThumbnailHierarchy(path, artistName, fallbackTrack);
                        App.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            HasCustomAvatar = isCustom;
                            HasValidImage = hasImg;
                            AvatarImage = img;
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

    public string Subtitle => $"{AlbumCount} álbumes • {TrackCount} canciones";

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

