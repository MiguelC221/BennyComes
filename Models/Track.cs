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
    [NotifyPropertyChangedFor(nameof(CoverImageSource))]
    private BitmapImage? _albumArtImage;

    private bool _isLoadingCover;

    public BitmapImage CoverImageSource
    {
        get
        {
            if (AlbumArtImage != null) return AlbumArtImage;

            if (Services.Metadata.MetadataService.Instance.TryGetMemoryCachedImage(this, 120, out var cached) && cached != null)
            {
                AlbumArtImage = cached;
                return cached;
            }

            if (!_isLoadingCover)
            {
                _isLoadingCover = true;
                Services.Metadata.MetadataService.Instance.QueueTrackThumbnailLoad(this, 120, img =>
                {
                    _isLoadingCover = false;
                    if (img != null)
                    {
                        AlbumArtImage = img;
                    }
                });
            }

            return Services.Metadata.MetadataService.Instance.GetFastPlaceholder(this);
        }
    }

    public void ReleaseArtFromMemory()
    {
        AlbumArtImage = null;
        AlbumArtData = null;
        _isLoadingCover = false;
    }

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

    public string ArtworkPath { get; set; } = string.Empty;
    public string FirstLetterGroup { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);

    public string AlphaGroup => !string.IsNullOrWhiteSpace(FirstLetterGroup)
        ? FirstLetterGroup
        : Helpers.AlphaGroupHelper.GetAlphaGroupKey(Title);

    private string? _sortTitleSource;
    private string _sortTitle = string.Empty;
    private int _groupRank;
    private string? _searchText;

    private void EnsureSortCache()
    {
        if (ReferenceEquals(_sortTitleSource, Title)) return;
        _sortTitleSource = Title;
        _sortTitle = Helpers.AlphaGroupHelper.GetSortKey(Title);
        var alpha = !string.IsNullOrWhiteSpace(FirstLetterGroup)
            ? FirstLetterGroup
            : Helpers.AlphaGroupHelper.GetAlphaGroupKey(Title);
        _groupRank = Helpers.AlphaGroupHelper.GetGroupRank(alpha);
    }

    public void SetPrecomputedSortAndSearch(string? sortTitle, int groupRank, string? searchText)
    {
        if (!string.IsNullOrEmpty(sortTitle))
        {
            _sortTitle = sortTitle;
            _groupRank = groupRank;
            _sortTitleSource = Title;
        }
        if (!string.IsNullOrEmpty(searchText))
        {
            _searchText = searchText;
        }
    }

    public string SortTitle { get { EnsureSortCache(); return _sortTitle; } }
    public int GroupRank { get { EnsureSortCache(); return _groupRank; } }

    public string SearchText => _searchText ??= Helpers.AlphaGroupHelper.NormalizeForSearch($"{Title} {Artist} {Album} {Genre}");

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
