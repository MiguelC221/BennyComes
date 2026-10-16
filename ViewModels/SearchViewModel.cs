using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "Todo"; // "Todo", "Canciones", "Artistas", "Álbumes"

    [ObservableProperty]
    private int _trackCount;

    [ObservableProperty]
    private int _artistCount;

    [ObservableProperty]
    private int _albumCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasSearchQuery;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _showArtistsSection;

    [ObservableProperty]
    private bool _showAlbumsSection;

    [ObservableProperty]
    private bool _showTracksSection;

    public ObservableCollection<Track> MatchingTracks { get; } = new();
    public ObservableCollection<ArtistModel> MatchingArtists { get; } = new();
    public ObservableCollection<AlbumModel> MatchingAlbums { get; } = new();

    public SearchViewModel()
    {
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += async (s, e) =>
        {
            _debounceTimer.Stop();
            await ExecuteSearchAsync();
        };

        _cache.LibraryCacheUpdated += () =>
        {
            if (HasSearchQuery)
            {
                _ = ExecuteSearchAsync();
            }
        };
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (MainViewModel.Instance != null && MainViewModel.Instance.SearchQuery != value)
        {
            MainViewModel.Instance.SearchQuery = value;
        }

        HasSearchQuery = !string.IsNullOrWhiteSpace(value);
        _debounceTimer.Stop();
        _searchCts?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearching = false;
            ClearResults();
        }
        else
        {
            IsSearching = true;
            _debounceTimer.Start();
        }
    }

    partial void OnSelectedFilterChanged(string value)
    {
        UpdateSectionVisibility();
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        if (MainViewModel.Instance != null)
        {
            MainViewModel.Instance.SearchQuery = string.Empty;
        }
        ClearResults();
    }

    public void RefreshResults()
    {
        if (HasSearchQuery)
        {
            _ = ExecuteSearchAsync();
        }
    }

    private void ClearResults()
    {
        var app = Application.Current;
        Action action = () =>
        {
            MatchingTracks.Clear();
            MatchingArtists.Clear();
            MatchingAlbums.Clear();
            TrackCount = 0;
            ArtistCount = 0;
            AlbumCount = 0;
            TotalCount = 0;
            HasResults = false;
            IsSearching = false;
            UpdateSectionVisibility();
        };

        if (app != null && !app.Dispatcher.CheckAccess())
            app.Dispatcher.Invoke(action);
        else
            action();
    }

    private async Task ExecuteSearchAsync()
    {
        string q = SearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
        {
            ClearResults();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;

        try
        {
            var (tracks, artists, albums, totalT, totalAr, totalAl) = await Task.Run(async () =>
            {
                // Ensure cache is loaded from SQLite if first time
                if (!_cache.IsLoaded)
                {
                    await _cache.InitializeAsync();
                }

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Filter Tracks: match Title, Artist, Album, Genre
                var allFoundTracks = _cache.CachedTracks.Where(t =>
                    terms.All(term =>
                        (t.Title?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.Artist?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.Album?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.Genre?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    )
                ).ToList();

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                // Filter Artists: match Name
                var allFoundArtists = _cache.CachedArtists.Where(a =>
                    terms.All(term =>
                        (a.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    )
                ).ToList();

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                // Filter Albums: match Name, Artist, Genre
                var allFoundAlbums = _cache.CachedAlbums.Where(al =>
                    terms.All(term =>
                        (al.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (al.Artist?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (al.Genre?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    )
                ).ToList();

                // Cap results displayed in unified view to prevent UI thread freezing
                var displayTracks = allFoundTracks.Take(100).ToList();
                var displayArtists = allFoundArtists.Take(30).ToList();
                var displayAlbums = allFoundAlbums.Take(30).ToList();

                return (displayTracks, displayArtists, displayAlbums, allFoundTracks.Count, allFoundArtists.Count, allFoundAlbums.Count);
            }, token);

            if (token.IsCancellationRequested) return;

            var app = Application.Current;
            Action updateUi = () =>
            {
                MatchingTracks.Clear();
                foreach (var t in tracks)
                    MatchingTracks.Add(t);

                MatchingArtists.Clear();
                foreach (var a in artists)
                    MatchingArtists.Add(a);

                MatchingAlbums.Clear();
                foreach (var al in albums)
                    MatchingAlbums.Add(al);

                TrackCount = totalT;
                ArtistCount = totalAr;
                AlbumCount = totalAl;
                TotalCount = totalT + totalAr + totalAl;
                HasResults = TotalCount > 0;
                IsSearching = false;

                UpdateSectionVisibility();
            };

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(updateUi);
            else
                updateUi();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
            IsSearching = false;
        }
    }

    private void UpdateSectionVisibility()
    {
        bool isAll = SelectedFilter == "Todo";
        ShowArtistsSection = !IsSearching && HasSearchQuery && ArtistCount > 0 && (isAll || SelectedFilter == "Artistas");
        ShowAlbumsSection = !IsSearching && HasSearchQuery && AlbumCount > 0 && (isAll || SelectedFilter == "Álbumes");
        ShowTracksSection = !IsSearching && HasSearchQuery && TrackCount > 0 && (isAll || SelectedFilter == "Canciones");
    }

    [RelayCommand]
    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        var list = MatchingTracks.ToList();
        int idx = list.IndexOf(track);
        _audio.PlayQueue(list, idx >= 0 ? idx : 0);
    }

    [RelayCommand]
    public void PlayAllTracks()
    {
        if (MatchingTracks.Count > 0)
        {
            _audio.PlayQueue(MatchingTracks.ToList(), 0);
        }
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null) return;
        MainViewModel.Instance?.PlayArtist(artist);
    }

    [RelayCommand]
    public void PlayAlbum(AlbumModel? album)
    {
        if (album == null) return;
        MainViewModel.Instance?.PlayAlbum(album);
    }
}
