using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class Playlist : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = "New Playlist";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    private int _trackCount;

    public ObservableCollection<Track> Tracks { get; } = new();

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _customCoverPath;

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _coverImage;

    public void RefreshCoverImage()
    {
        if (!string.IsNullOrEmpty(CustomCoverPath) && System.IO.File.Exists(CustomCoverPath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(CustomCoverPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                CoverImage = bmp;
                return;
            }
            catch { }
        }
        CoverImage = null;
    }

    public string CreatedAtFormatted => CreatedAt.ToLocalTime().ToString("MMM dd, yyyy");
}
