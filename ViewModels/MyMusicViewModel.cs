using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BenyComes.ViewModels;

public partial class MyMusicViewModel : ObservableObject
{
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    private const int PageSize = 30;
    private List<AlbumModel> _allFilteredAlbums = new();
    private List<ArtistModel> _allFilteredArtists = new();
    private int _loadedAlbumsCount = 0;
    private int _loadedArtistsCount = 0;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Canciones, 1 = Artistas, 2 = Álbumes

    [ObservableProperty]
    private int _totalTracksCount;

    // Filters
    public ObservableCollection<string> SortOptions { get; } = new();

    [ObservableProperty]
    private string _selectedSort = "Nombre (A-Z)";

    [ObservableProperty]
    private bool _isSortDescending = false;

    public string SortDirectionTooltip => IsSortDescending ? "Orden descendente (clic para ascendente)" : "Orden ascendente (clic para descendente)";

    public SymbolRegular SortDirectionIcon => IsSortDescending ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowUp24;

    public ObservableCollection<string> Genres { get; } = new();

    [ObservableProperty]
    private string _selectedGenre = "Todos los géneros";

    // Collections
    public ObservableCollection<Track> Songs { get; } = new();
    public ObservableCollection<ArtistModel> Artists { get; } = new();
    public ObservableCollection<AlbumModel> Albums { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AlbumModel? _selectedAlbumDetail;

    [ObservableProperty]
    private ArtistModel? _selectedArtistDetail;

    public bool IsInDetailView => SelectedAlbumDetail != null || SelectedArtistDetail != null;

    partial void OnSelectedAlbumDetailChanged(AlbumModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
    }

    partial void OnSelectedArtistDetailChanged(ArtistModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
    }

    private bool _suppressFilterTriggers;

    public string ShuffleButtonText
    {
        get
        {
            return SelectedTabIndex switch
            {
                1 => $"Reproducir aleatoriamente ({_allFilteredArtists.Count})",
                2 => $"Reproducir aleatoriamente ({_allFilteredAlbums.Count})",
                _ => $"Reproducir aleatoriamente ({TotalTracksCount})"
            };
        }
    }

    public MyMusicViewModel()
    {
        UpdateSortOptions();
        _cache.LibraryCacheUpdated += OnLibraryCacheUpdated;
        _ = InitializeAsync();
    }

    private void OnLibraryCacheUpdated()
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            LoadGenresFromCache();
            RefreshData();
        });
    }

    private void UpdateSortOptions()
    {
        _suppressFilterTriggers = true;
        SortOptions.Clear();

        if (SelectedTabIndex == 1) // Artistas
        {
            SortOptions.Add("Nombre (A-Z)");
            SortOptions.Add("Cantidad de canciones");
            SortOptions.Add("Cantidad de álbumes");
            SelectedSort = "Nombre (A-Z)";
        }
        else if (SelectedTabIndex == 2) // Álbumes
        {
            SortOptions.Add("Fecha de adición");
            SortOptions.Add("Nombre (A-Z)");
            SortOptions.Add("Artista");
            SortOptions.Add("Año");
            SelectedSort = "Fecha de adición";
        }
        else // Canciones
        {
            SortOptions.Add("Nombre (A-Z)");
            SortOptions.Add("Álbum");
            SortOptions.Add("Duración");
            SortOptions.Add("Fecha modificada");
            SortOptions.Add("Fecha de adición");
            SortOptions.Add("Género musical");
            SelectedSort = "Nombre (A-Z)";
        }
        _suppressFilterTriggers = false;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        SelectedAlbumDetail = null;
        SelectedArtistDetail = null;
        UpdateSortOptions();
        OnPropertyChanged(nameof(ShuffleButtonText));
    }

    private async Task InitializeAsync()
    {
        await _cache.InitializeAsync();
        LoadGenresFromCache();
        RefreshData();
    }

    private void LoadGenresFromCache()
    {
        Genres.Clear();
        Genres.Add("Todos los géneros");
        foreach (var g in _cache.CachedGenres)
        {
            if (!string.IsNullOrWhiteSpace(g) && !Genres.Contains(g))
            {
                Genres.Add(g);
            }
        }

        if (!Genres.Contains(SelectedGenre))
        {
            _suppressFilterTriggers = true;
            SelectedGenre = "Todos los géneros";
            _suppressFilterTriggers = false;
        }
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        IsSortDescending = !IsSortDescending;
        OnPropertyChanged(nameof(SortDirectionTooltip));
        OnPropertyChanged(nameof(SortDirectionIcon));
        RefreshData();
    }

    [RelayCommand]
    public void RefreshData()
    {
        IsLoading = true;
        try
        {
            string sortKey = SelectedSort;

            // Instant in-memory queries
            var tracks = _cache.GetFilteredTracks(sortKey, SelectedGenre, string.Empty, IsSortDescending);
            Songs.Clear();
            foreach (var t in tracks)
            {
                Songs.Add(t);
            }
            TotalTracksCount = tracks.Count;

            _allFilteredAlbums = _cache.GetFilteredAlbums(sortKey, SelectedGenre, string.Empty, IsSortDescending);
            Albums.Clear();
            _loadedAlbumsCount = 0;
            var initialAlbums = _allFilteredAlbums.Take(PageSize).ToList();
            foreach (var a in initialAlbums)
            {
                Albums.Add(a);
            }
            _loadedAlbumsCount = initialAlbums.Count;

            _allFilteredArtists = _cache.GetFilteredArtists(sortKey, string.Empty, IsSortDescending);
            Artists.Clear();
            _loadedArtistsCount = 0;
            var initialArtists = _allFilteredArtists.Take(PageSize).ToList();
            foreach (var ar in initialArtists)
            {
                Artists.Add(ar);
            }
            _loadedArtistsCount = initialArtists.Count;

            OnPropertyChanged(nameof(ShuffleButtonText));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void LoadMoreAlbums()
    {
        if (_loadedAlbumsCount >= _allFilteredAlbums.Count) return;

        var nextBatch = _allFilteredAlbums.Skip(_loadedAlbumsCount).Take(PageSize).ToList();
        foreach (var a in nextBatch)
        {
            Albums.Add(a);
        }
        _loadedAlbumsCount += nextBatch.Count;
    }

    [RelayCommand]
    public void LoadMoreArtists()
    {
        if (_loadedArtistsCount >= _allFilteredArtists.Count) return;

        var nextBatch = _allFilteredArtists.Skip(_loadedArtistsCount).Take(PageSize).ToList();
        foreach (var ar in nextBatch)
        {
            Artists.Add(ar);
        }
        _loadedArtistsCount += nextBatch.Count;
    }

    public async Task RefreshDataAsync()
    {
        RefreshData();
        await Task.CompletedTask;
    }

    partial void OnSelectedSortChanged(string value)
    {
        if (_suppressFilterTriggers) return;
        RefreshData();
    }

    partial void OnSelectedGenreChanged(string value)
    {
        if (_suppressFilterTriggers) return;
        RefreshData();
    }

    [RelayCommand]
    public void PlayTrack(Track? track)
    {
        if (track == null) return;

        IEnumerable<Track> sourceList = Songs;
        if (SelectedAlbumDetail != null && SelectedAlbumDetail.Tracks.Any(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            sourceList = SelectedAlbumDetail.Tracks;
        }
        else if (SelectedArtistDetail != null && SelectedArtistDetail.Tracks.Any(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            sourceList = SelectedArtistDetail.Tracks;
        }

        _audio.ReplaceQueueAndPlay(sourceList, track, _audio.IsShuffle);
    }

    [RelayCommand]
    private void PlayAllShuffled()
    {
        List<Track> pool;
        if (SelectedTabIndex == 1) // Artistas
        {
            pool = _allFilteredArtists.SelectMany(a => a.Tracks).DistinctBy(t => t.FilePath).ToList();
        }
        else if (SelectedTabIndex == 2) // Álbumes
        {
            pool = _allFilteredAlbums.SelectMany(a => a.Tracks).DistinctBy(t => t.FilePath).ToList();
        }
        else // Canciones
        {
            pool = Songs.ToList();
        }

        if (pool.Count == 0) return;

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        _audio.IsShuffle = true;
        _audio.PlayQueue(pool, 0);
    }

    [RelayCommand]
    public void OpenAlbumDetail(AlbumModel? album)
    {
        if (album == null) return;
        SelectedAlbumDetail = album;
    }

    [RelayCommand]
    public void CloseAlbumDetail()
    {
        SelectedAlbumDetail = null;
    }

    [RelayCommand]
    public void OpenArtistDetail(ArtistModel? artist)
    {
        if (artist == null) return;
        SelectedArtistDetail = artist;
    }

    [RelayCommand]
    public void CloseArtistDetail()
    {
        SelectedArtistDetail = null;
    }

    [RelayCommand]
    public void PlayAlbum(AlbumModel? album)
    {
        if (album == null) return;
        var tracks = album.Tracks;
        if (tracks.Count == 0)
        {
            tracks = LibraryCacheService.Instance.CachedTracks.Where(t => string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (tracks.Count == 0) return;
        _audio.PlayQueue(tracks, 0);
    }

    [RelayCommand]
    public void AddAlbumToQueue(AlbumModel? album)
    {
        if (album == null) return;
        var tracks = album.Tracks.Count > 0 ? album.Tracks : LibraryCacheService.Instance.CachedTracks.Where(t => string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tracks.Count == 0) return;
        _audio.AddRangeToQueue(tracks, playFirst: false);
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null) return;
        var tracks = artist.Tracks;
        if (tracks.Count == 0)
        {
            tracks = LibraryCacheService.Instance.CachedTracks.Where(t => string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (tracks.Count == 0) return;
        _audio.PlayQueue(tracks, 0);
    }

    [RelayCommand]
    public void AddArtistToQueue(ArtistModel? artist)
    {
        if (artist == null) return;
        var tracks = artist.Tracks.Count > 0 ? artist.Tracks : LibraryCacheService.Instance.CachedTracks.Where(t => string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tracks.Count == 0) return;
        _audio.AddRangeToQueue(tracks, playFirst: false);
    }
}
