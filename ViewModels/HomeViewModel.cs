using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public class GenreMixItem
{
    public string GenreName { get; set; } = string.Empty;
    public string Title => $"Mix {GenreName}";
    public int TrackCount { get; set; }
    public string Subtitle => $"{TrackCount} canciones";
    public BitmapImage? CoverImage { get; set; }
}

public class SmartMixItemModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryTag { get; set; } = "RECOMENDADO";
    public string SymbolName { get; set; } = "MusicNote224";
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; set; } = Wpf.Ui.Controls.SymbolRegular.MusicNote224;
    public string AccentColor { get; set; } = "#FF00D26A";
    public string BadgeBackgroundColor { get; set; } = "#2600D26A";
    public Brush CardBackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush CardBorderBrush { get; set; } = Brushes.Transparent;
    public BitmapImage? CoverImage { get; set; }
    public IRelayCommand Command { get; set; } = null!;
    public object? CommandParameter { get; set; }
}

public partial class HomeViewModel : ObservableObject
{
    private readonly LibraryDao _dao = LibraryDao.Instance;
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    [ObservableProperty]
    private string _greeting = "¡Hola de nuevo!";

    [ObservableProperty]
    private string _selectedPeriod = "Month"; // Month, Year, AllTime

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private int _favoriteCount = 0;

    [ObservableProperty]
    private int _smartMixColumns = 3;

    public ObservableCollection<Track> TopTracks { get; } = new();
    public ObservableCollection<ArtistModel> TopArtists { get; } = new();
    public ObservableCollection<AlbumModel> TopAlbums { get; } = new();
    public ObservableCollection<GenreMixItem> GenreMixes { get; } = new();
    public ObservableCollection<SmartMixItemModel> SmartMixes { get; } = new();

    public HomeViewModel()
    {
        UpdateGreeting();
    }

    public void UpdateGreeting()
    {
        int hour = DateTime.Now.Hour;
        Greeting = hour switch
        {
            >= 5 and < 12 => "¡Buenos días!",
            >= 12 and < 20 => "¡Buenas tardes!",
            _ => "¡Buenas noches!"
        };
    }

    public async Task LoadHomeDataAsync()
    {
        IsLoading = true;
        UpdateGreeting();

        try
        {
            // Update Favorites count
            var favs = await _dao.GetFavoriteTracksAsync();
            FavoriteCount = favs.Count;

            // Load Period Stats
            await LoadPeriodStatsAsync(SelectedPeriod);

            // Load Smart Genre Mixes
            await LoadGenreMixesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading Home data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SetPeriodAsync(string period)
    {
        SelectedPeriod = period;
        await LoadPeriodStatsAsync(period);
    }

    private async Task LoadPeriodStatsAsync(string period)
    {
        var tracks = await _dao.GetTopTracksAsync(period, 15);
        TopTracks.Clear();
        int rank = 1;
        foreach (var t in tracks)
        {
            t.Rank = rank++;
            t.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(t);
            TopTracks.Add(t);
        }

        // Top Artists based on top tracks & cache
        var topArtistNames = tracks
            .GroupBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => g.Key)
            .ToList();

        TopArtists.Clear();
        foreach (var name in topArtistNames)
        {
            var cached = _cache.CachedArtists.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cached != null)
            {
                TopArtists.Add(cached);
            }
            else
            {
                var artistTracks = tracks.Where(t => t.Artist.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var tr = artistTracks.FirstOrDefault();
                var am = new ArtistModel
                {
                    Name = name,
                    AvatarImage = tr != null ? MetadataService.Instance.GetAlbumArtImage(tr) : null
                };
                foreach (var t in artistTracks) am.Tracks.Add(t);
                TopArtists.Add(am);
            }
        }

        // Top Albums based on top tracks & cache
        var topAlbumNames = tracks
            .GroupBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => g.Key)
            .ToList();

        TopAlbums.Clear();
        foreach (var name in topAlbumNames)
        {
            var cached = _cache.CachedAlbums.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cached != null)
            {
                TopAlbums.Add(cached);
            }
            else
            {
                var albumTracks = tracks.Where(t => t.Album.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var tr = albumTracks.FirstOrDefault();
                var alb = new AlbumModel
                {
                    Name = name,
                    Artist = tr?.Artist ?? "Varios Artistas",
                    CoverImage = tr != null ? MetadataService.Instance.GetAlbumArtImage(tr) : null
                };
                foreach (var t in albumTracks) alb.Tracks.Add(t);
                TopAlbums.Add(alb);
            }
        }
    }

    private async Task LoadGenreMixesAsync()
    {
        var topGenres = await _dao.GetTopGenresAsync(3);
        GenreMixes.Clear();

        foreach (var genre in topGenres)
        {
            var genreTracks = await _dao.GetTracksByGenreAsync(genre, 30);
            var firstTrack = genreTracks.FirstOrDefault();
            var art = firstTrack != null ? MetadataService.Instance.GetAlbumArtImage(firstTrack) : null;

            GenreMixes.Add(new GenreMixItem
            {
                GenreName = genre,
                TrackCount = genreTracks.Count,
                CoverImage = art
            });
        }
        BuildSmartMixCards();
    }

    private static LinearGradientBrush CreateMixGradient(string hexColor)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(hexColor);
        var startColor = Color.FromArgb(48, baseColor.R, baseColor.G, baseColor.B);
        var midColor = Color.FromArgb(22, baseColor.R, baseColor.G, baseColor.B);
        var endColor = Color.FromArgb(245, 18, 18, 24);

        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(startColor, 0.0),
                new GradientStop(midColor, 0.45),
                new GradientStop(endColor, 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateBorderBrush(string hexColor)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(hexColor);
        var brush = new SolidColorBrush(Color.FromArgb(85, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        return brush;
    }

    public void BuildSmartMixCards()
    {
        SmartMixes.Clear();

        // 1. Favoritos
        SmartMixes.Add(new SmartMixItemModel
        {
            Title = "Tus Favoritos",
            Subtitle = "Mix de guardadas",
            Description = $"{FavoriteCount} canciones",
            CategoryTag = "PERSONAL",
            SymbolName = "Heart24",
            Symbol = Wpf.Ui.Controls.SymbolRegular.Heart24,
            AccentColor = "#FFFF3366",
            BadgeBackgroundColor = "#33FF3366",
            CardBackgroundBrush = CreateMixGradient("#FFFF3366"),
            CardBorderBrush = CreateBorderBrush("#FFFF3366"),
            Command = PlayMixFavoritesCommand
        });

        // 2. Más Escuchadas
        SmartMixes.Add(new SmartMixItemModel
        {
            Title = "Más Escuchadas",
            Subtitle = "Tus hits del mes",
            Description = "Las que más suenan en tu lista",
            CategoryTag = "TENDENCIA",
            SymbolName = "Fire24",
            Symbol = Wpf.Ui.Controls.SymbolRegular.Fire24,
            AccentColor = "#FFFF8800",
            BadgeBackgroundColor = "#33FF8800",
            CardBackgroundBrush = CreateMixGradient("#FFFF8800"),
            CardBorderBrush = CreateBorderBrush("#FFFF8800"),
            Command = PlayMixMonthCommand
        });

        // 3. Joyas Olvidadas
        SmartMixes.Add(new SmartMixItemModel
        {
            Title = "Joyas Olvidadas",
            Subtitle = "Redescubre temas",
            Description = "Pistas con pocas reproducciones",
            CategoryTag = "REDESCUBRE",
            SymbolName = "Sparkle24",
            Symbol = Wpf.Ui.Controls.SymbolRegular.Sparkle24,
            AccentColor = "#FF00B4D8",
            BadgeBackgroundColor = "#3300B4D8",
            CardBackgroundBrush = CreateMixGradient("#FF00B4D8"),
            CardBorderBrush = CreateBorderBrush("#FF00B4D8"),
            Command = PlayMixGemsCommand
        });

        // 4. Géneros Populares
        var genreColors = new[] { "#FF8B5CF6", "#FF10B981", "#FFEC4899", "#FF3B82F6", "#FFF59E0B" };
        int colorIdx = 0;
        foreach (var gm in GenreMixes)
        {
            string hex = genreColors[colorIdx % genreColors.Length];
            colorIdx++;

            SmartMixes.Add(new SmartMixItemModel
            {
                Title = gm.Title,
                Subtitle = "Mix por género",
                Description = $"{gm.TrackCount} canciones",
                CategoryTag = "GÉNERO",
                SymbolName = "MusicNote224",
                Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote224,
                AccentColor = hex,
                BadgeBackgroundColor = hex.Replace("#FF", "#33"),
                CardBackgroundBrush = CreateMixGradient(hex),
                CardBorderBrush = CreateBorderBrush(hex),
                CoverImage = gm.CoverImage,
                Command = PlayGenreMixCommand,
                CommandParameter = gm.GenreName
            });
        }
    }

    [RelayCommand]
    public void ResumePlayback()
    {
        if (_audio.IsPlaying) return;
        if (_audio.CurrentTrack != null)
        {
            _audio.Play();
        }
        else if (_audio.Queue.Count > 0)
        {
            _audio.PlayTrackAt(0);
        }
        else if (_cache.CachedTracks.Count > 0)
        {
            DailyRandomMix();
        }
    }

    [RelayCommand]
    public void DailyRandomMix()
    {
        var all = _cache.CachedTracks;
        if (all.Count == 0) return;

        var pool = new List<Track>(all);
        var rng = new Random();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        _audio.PlayQueue(pool.Take(50), 0);
    }

    [RelayCommand]
    public void PlayTopAsPlaylist()
    {
        if (TopTracks.Count > 0)
        {
            _audio.PlayQueue(TopTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        var list = TopTracks.ToList();
        int idx = list.IndexOf(track);
        if (idx >= 0 && list.Count > 0)
        {
            _audio.PlayQueue(list, idx);
        }
        else
        {
            _audio.PlayTrack(track);
        }
        MainViewModel.Instance?.ForceUiPlaybackUpdate(track, true);
    }

    [RelayCommand]
    public async Task PlayMixFavoritesAsync()
    {
        var favs = await _dao.GetFavoriteTracksAsync();
        if (favs.Count > 0)
        {
            _audio.PlayQueue(favs, 0);
        }
    }

    [RelayCommand]
    public async Task PlayMixMonthAsync()
    {
        var top = await _dao.GetTopTracksAsync("Month", 30);
        if (top.Count > 0)
        {
            _audio.PlayQueue(top, 0);
        }
    }

    [RelayCommand]
    public async Task PlayMixGemsAsync()
    {
        var gems = await _dao.GetForgottenGemsAsync(30);
        if (gems.Count > 0)
        {
            _audio.PlayQueue(gems, 0);
        }
    }

    [RelayCommand]
    public async Task PlayGenreMixAsync(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre)) return;
        var tracks = await _dao.GetTracksByGenreAsync(genre, 40);
        if (tracks.Count > 0)
        {
            _audio.PlayQueue(tracks, 0);
        }
    }

    [RelayCommand]
    public void PlayTopArtists()
    {
        var tracks = TopArtists.SelectMany(a => a.Tracks).DistinctBy(t => t.FilePath).ToList();
        if (tracks.Count == 0)
        {
            tracks = _cache.CachedTracks.Where(t => TopArtists.Any(a => a.Name.Equals(t.Artist, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        if (tracks.Count > 0)
        {
            _audio.PlayQueue(tracks, 0);
        }
    }

    [RelayCommand]
    public void PlayTopAlbums()
    {
        var tracks = TopAlbums.SelectMany(a => a.Tracks).DistinctBy(t => t.FilePath).ToList();
        if (tracks.Count == 0)
        {
            tracks = _cache.CachedTracks.Where(t => TopAlbums.Any(a => a.Name.Equals(t.Album, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        if (tracks.Count > 0)
        {
            _audio.PlayQueue(tracks, 0);
        }
    }
}
