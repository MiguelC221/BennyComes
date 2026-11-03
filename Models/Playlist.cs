using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class Playlist : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = "New Playlist";

    [ObservableProperty]
    private bool _isSelectedInSidebar;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private int _trackCount;

    public string Subtitle => TrackCount == 1 ? "1 canción" : $"{TrackCount} canciones";

    public ObservableCollection<Track> Tracks { get; } = new();

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _customCoverPath;

    public string? FirstTrackFilePath { get; set; }
    public string? FirstTrackArtworkPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoverImage))]
    private System.Windows.Media.ImageSource? _coverImage;

    public bool HasCoverImage => CoverImage != null;

    public void RefreshCoverImage()
    {
        if (!string.IsNullOrEmpty(CustomCoverPath) && System.IO.File.Exists(CustomCoverPath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 240;
                bmp.UriSource = new Uri(CustomCoverPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                CoverImage = bmp;
                return;
            }
            catch { }
        }

        // Fallback to first track cover if available
        var firstTrack = Tracks.Count > 0 ? Tracks[0] : null;
        string? artPath = firstTrack?.ArtworkPath ?? FirstTrackArtworkPath;
        if (!string.IsNullOrEmpty(artPath) && System.IO.File.Exists(artPath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 240;
                bmp.UriSource = new Uri(artPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                CoverImage = bmp;
                return;
            }
            catch { }
        }

        string? filePath = firstTrack?.FilePath ?? FirstTrackFilePath;
        if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
        {
            try
            {
                var img = Services.Metadata.MetadataService.Instance.GetAlbumArtImage(new Track
                {
                    FilePath = filePath,
                    ArtworkPath = artPath ?? string.Empty
                });
                if (img != null)
                {
                    CoverImage = img;
                    return;
                }
            }
            catch { }
        }

        CoverImage = null;
    }

    public string CreatedAtFormatted => CreatedAt.ToLocalTime().ToString("MMM dd, yyyy");
}
