using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

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

    public BitmapImage CoverImageSource => CoverImage ??= LoadThumbnail(ArtworkPath, Tracks.FirstOrDefault());

    public string Subtitle => Year > 0 ? $"{Artist} • {Year}" : Artist;

    private static BitmapImage LoadThumbnail(string? path, Track? fallbackTrack)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
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
            catch { }
        }

        return Services.Metadata.MetadataService.Instance.GetAlbumArtImage(fallbackTrack, decodeWidth: 180);
    }
}

public partial class ArtistModel : ObservableObject
{
    public string Name { get; set; } = "Unknown Artist";
    public string? AvatarPath { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public List<AlbumModel> Albums { get; set; } = new();

    public int PersistedTrackCount { get; set; }
    public int PersistedAlbumCount { get; set; }

    public int TrackCount => Tracks.Count > 0 ? Tracks.Count : PersistedTrackCount;
    public int AlbumCount => Albums.Count > 0 ? Albums.Count : (Tracks.Count > 0 ? Tracks.Select(t => t.Album).Distinct().Count() : PersistedAlbumCount);

    [ObservableProperty]
    private BitmapImage? _avatarImage;

    public BitmapImage AvatarImageSource => AvatarImage ??= LoadThumbnail(AvatarPath, Tracks.FirstOrDefault());

    public string Subtitle => $"{AlbumCount} álbumes • {TrackCount} canciones";

    private static BitmapImage LoadThumbnail(string? path, Track? fallbackTrack)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
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
            catch { }
        }

        return Services.Metadata.MetadataService.Instance.GetAlbumArtImage(fallbackTrack, decodeWidth: 180);
    }
}
