using System;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class Track : ObservableObject
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = "Unknown Title";
    public string Artist { get; set; } = "Unknown Artist";
    public string Album { get; set; } = "Unknown Album";
    public uint Year { get; set; }
    public uint TrackNumber { get; set; }
    public string Genre { get; set; } = "Unknown";
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public int Bitrate { get; set; } // in kbps
    public int SampleRate { get; set; } = 44100; // in Hz
    public int BitsPerSample { get; set; } = 16;
    public int Channels { get; set; } = 2;
    public string FileFormat { get; set; } = "FLAC";
    public byte[]? AlbumArtData { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    public int PlayCount { get; set; }
    public string AlbumArtist { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Rank { get; set; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private BitmapImage? _albumArtImage;

    public BitmapImage CoverImageSource => AlbumArtImage ??= Services.Metadata.MetadataService.Instance.GetAlbumArtImage(this);

    public string ArtistAndAlbum => string.IsNullOrWhiteSpace(Album) || Album == "Unknown Album"
        ? Artist
        : $"{Artist} • {Album}";

    public string DurationFormatted => Duration.Hours > 0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public string SampleRateFormatted => SampleRate >= 1000
        ? $"{(SampleRate / 1000.0):0.#} kHz"
        : $"{SampleRate} Hz";

    public string BitDepthFormatted => $"{BitsPerSample}-bit";

    public string FileName => Path.GetFileName(FilePath);

    public override bool Equals(object? obj)
    {
        if (obj is Track other)
        {
            if (Id > 0 && other.Id > 0) return Id == other.Id;
            return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public override int GetHashCode()
    {
        if (Id > 0) return Id.GetHashCode();
        return StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath ?? string.Empty);
    }
}
