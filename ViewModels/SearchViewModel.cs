using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BenyComes.ViewModels;

public partial class SearchSuggestionItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string SuggestionType { get; set; } = "Search"; // "Search", "Track", "Artist", "Album"
    public object? Target { get; set; }
    public SymbolRegular Icon { get; set; } = SymbolRegular.Search24;
    public System.Windows.Media.ImageSource? CoverImage { get; set; }
    public bool HasCoverImage => CoverImage != null;
    public bool IsArtist => SuggestionType == "Artist";

    [ObservableProperty]
    private bool _isSelected;
}

public partial class SearchViewModel : ObservableObject
{
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    private readonly DispatcherTimer _suggestionsDebounceTimer;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _suggestionsCts;

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
    private bool _isSuggestionsOpen;

    [ObservableProperty]
    private bool _isSidebarSuggestionsOpen;

    [ObservableProperty]
    private bool _isPageSuggestionsOpen;

    [ObservableProperty]
    private string _activeSearchSource = string.Empty;

    [ObservableProperty]
    private int _selectedSuggestionIndex = -1;

    partial void OnSelectedSuggestionIndexChanged(int value)
    {
        for (int i = 0; i < Suggestions.Count; i++)
        {
            Suggestions[i].IsSelected = (i == value);
        }
    }

    partial void OnActiveSearchSourceChanged(string value)
    {
        UpdateSuggestionsPopupVisibility();
    }

    private void UpdateSuggestionsPopupVisibility()
    {
        bool hasSuggestions = Suggestions.Count > 0 && HasSearchQuery;
        IsSidebarSuggestionsOpen = hasSuggestions && ActiveSearchSource == "Sidebar";
        IsPageSuggestionsOpen = hasSuggestions && ActiveSearchSource == "Page";
    }

    [ObservableProperty]
    private bool _showArtistsSection;

    [ObservableProperty]
    private bool _showAlbumsSection;

    [ObservableProperty]
    private bool _showTracksSection;

    public BulkObservableCollection<Track> MatchingTracks { get; } = new();
    public BulkObservableCollection<ArtistModel> MatchingArtists { get; } = new();
    public BulkObservableCollection<AlbumModel> MatchingAlbums { get; } = new();
    public BulkObservableCollection<SearchSuggestionItem> Suggestions { get; } = new();

    public SearchViewModel()
    {
        _suggestionsDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _suggestionsDebounceTimer.Tick += async (s, e) =>
        {
            _suggestionsDebounceTimer.Stop();
            await UpdateSuggestionsAsync();
        };

        _cache.LibraryCacheUpdated += () =>
        {
            if (HasSearchQuery && HasResults)
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
        _suggestionsDebounceTimer.Stop();
        _suggestionsCts?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearching = false;
            IsSuggestionsOpen = false;
            Suggestions.Clear();
            ClearResults();
        }
        else
        {
            // Lightweight suggestions timer; do not run full search until Enter is pressed
            _suggestionsDebounceTimer.Start();
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
        _suggestionsDebounceTimer.Stop();
        _suggestionsCts?.Cancel();
        _searchCts?.Cancel();

        SearchQuery = string.Empty;
        if (MainViewModel.Instance != null)
        {
            MainViewModel.Instance.SearchQuery = string.Empty;
        }

        Suggestions.Clear();
        IsSuggestionsOpen = false;
        ClearResults();
    }

    [RelayCommand]
    public void MoveSuggestionSelection(int delta)
    {
        if (Suggestions.Count == 0)
        {
            SelectedSuggestionIndex = -1;
            return;
        }

        int newIdx = SelectedSuggestionIndex + delta;
        if (newIdx < 0)
        {
            newIdx = -1;
        }
        else if (newIdx >= Suggestions.Count)
        {
            newIdx = Suggestions.Count - 1;
        }

        SelectedSuggestionIndex = newIdx;
    }

    [RelayCommand]
    public void SelectCurrentHighlightedSuggestion()
    {
        if (SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count)
        {
            var item = Suggestions[SelectedSuggestionIndex];
            SelectSuggestion(item);
        }
    }

    [RelayCommand]
    public void CloseSuggestions()
    {
        IsSuggestionsOpen = false;
        IsSidebarSuggestionsOpen = false;
        IsPageSuggestionsOpen = false;
        SelectedSuggestionIndex = -1;
    }

    [RelayCommand]
    public async Task ExecuteFullSearchAsync()
    {
        _suggestionsDebounceTimer.Stop();
        _suggestionsCts?.Cancel();
        IsSuggestionsOpen = false;
        IsSidebarSuggestionsOpen = false;
        IsPageSuggestionsOpen = false;

        string q = SearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
        {
            ClearResults();
            return;
        }

        if (MainViewModel.Instance != null && MainViewModel.Instance.CurrentView != "Search")
        {
            MainViewModel.Instance.NavigateToCommand.Execute("Search");
        }

        await ExecuteSearchAsync();
    }

    [RelayCommand]
    public void SelectSuggestion(SearchSuggestionItem? item)
    {
        if (item == null) return;
        IsSuggestionsOpen = false;
        IsSidebarSuggestionsOpen = false;
        IsPageSuggestionsOpen = false;

        switch (item.SuggestionType)
        {
            case "Search":
                _ = ExecuteFullSearchAsync();
                break;

            case "Track":
                if (item.Target is Track track)
                {
                    _audio.PlayTrack(track);
                }
                break;

            case "Artist":
                if (item.Target is ArtistModel artist)
                {
                    MainViewModel.Instance?.NavigateToCommand.Execute("MyMusic");
                    MainViewModel.Instance?.MyMusic.OpenArtistDetail(artist);
                }
                break;

            case "Album":
                if (item.Target is AlbumModel album)
                {
                    MainViewModel.Instance?.NavigateToCommand.Execute("MyMusic");
                    MainViewModel.Instance?.MyMusic.OpenAlbumDetail(album);
                }
                break;
        }
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

    private async Task UpdateSuggestionsAsync()
    {
        string q = SearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q) || q.Length < 1)
        {
            Suggestions.Clear();
            IsSuggestionsOpen = false;
            return;
        }

        _suggestionsCts?.Cancel();
        _suggestionsCts = new CancellationTokenSource();
        var token = _suggestionsCts.Token;

        try
        {
            var suggestions = await Task.Run(async () =>
            {
                if (!_cache.IsLoaded)
                {
                    await _cache.InitializeAsync();
                }

                if (token.IsCancellationRequested) return new List<SearchSuggestionItem>();

                string normQuery = AlphaGroupHelper.NormalizeForSearch(q);
                var terms = normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0) return new List<SearchSuggestionItem>();

                static bool MatchesAllTerms(string searchText, string[] queryTerms)
                {
                    if (string.IsNullOrEmpty(searchText)) return false;
                    for (int i = 0; i < queryTerms.Length; i++)
                    {
                        if (!searchText.Contains(queryTerms[i], StringComparison.Ordinal))
                            return false;
                    }
                    return true;
                }

                var list = new List<SearchSuggestionItem>();

                // Primary item: Search all
                list.Add(new SearchSuggestionItem
                {
                    Title = $"Buscar \"{q}\" en todo",
                    Subtitle = "Presiona Enter para buscar en canciones, artistas y álbumes",
                    SuggestionType = "Search",
                    Icon = SymbolRegular.Search24
                });

                // Top artists (up to 2)
                var cachedArtists = _cache.CachedArtists;
                int artistMatches = 0;
                for (int i = 0; i < cachedArtists.Count && artistMatches < 2; i++)
                {
                    var a = cachedArtists[i];
                    if (MatchesAllTerms(a.SearchText, terms))
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            Title = a.Name,
                            Subtitle = a.TrackCount == 1 ? "Artista • 1 canción" : $"Artista • {a.TrackCount} canciones",
                            SuggestionType = "Artist",
                            Target = a,
                            Icon = SymbolRegular.Person24
                        });
                        artistMatches++;
                    }
                }

                if (token.IsCancellationRequested) return new List<SearchSuggestionItem>();

                // Top albums (up to 2)
                var cachedAlbums = _cache.CachedAlbums;
                int albumMatches = 0;
                for (int i = 0; i < cachedAlbums.Count && albumMatches < 2; i++)
                {
                    var al = cachedAlbums[i];
                    if (MatchesAllTerms(al.SearchText, terms))
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            Title = al.Name,
                            Subtitle = $"Álbum • {al.Artist}",
                            SuggestionType = "Album",
                            Target = al,
                            Icon = SymbolRegular.Album24,
                            CoverImage = al.CoverImageSource
                        });
                        albumMatches++;
                    }
                }

                if (token.IsCancellationRequested) return new List<SearchSuggestionItem>();

                // Top tracks (up to 4)
                var cachedTracks = _cache.CachedTracks;
                int trackMatches = 0;
                for (int i = 0; i < cachedTracks.Count && trackMatches < 4; i++)
                {
                    var t = cachedTracks[i];
                    if (MatchesAllTerms(t.SearchText, terms))
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            Title = t.Title,
                            Subtitle = $"Canción • {t.Artist}",
                            SuggestionType = "Track",
                            Target = t,
                            Icon = SymbolRegular.MusicNote224,
                            CoverImage = t.CoverImageSource
                        });
                        trackMatches++;
                    }
                }

                return list;
            }, token);

            if (token.IsCancellationRequested) return;

            var app = Application.Current;
            Action apply = () =>
            {
                if (string.IsNullOrWhiteSpace(SearchQuery))
                {
                    Suggestions.Clear();
                    SelectedSuggestionIndex = -1;
                    IsSuggestionsOpen = false;
                    IsSidebarSuggestionsOpen = false;
                    IsPageSuggestionsOpen = false;
                }
                else
                {
                    SelectedSuggestionIndex = -1;
                    Suggestions.ReplaceRange(suggestions);
                    IsSuggestionsOpen = suggestions.Count > 0;
                    UpdateSuggestionsPopupVisibility();
                }
            };

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(apply);
            else
                apply();
        }
        catch (OperationCanceledException) { }
        catch { }
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
                if (!_cache.IsLoaded)
                {
                    await _cache.InitializeAsync();
                }

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                string normQuery = AlphaGroupHelper.NormalizeForSearch(q);
                var terms = normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                static bool MatchesAllTerms(string searchText, string[] queryTerms)
                {
                    if (string.IsNullOrEmpty(searchText)) return false;
                    for (int i = 0; i < queryTerms.Length; i++)
                    {
                        if (!searchText.Contains(queryTerms[i], StringComparison.Ordinal))
                            return false;
                    }
                    return true;
                }

                var cachedTracks = _cache.CachedTracks;
                var allFoundTracks = new List<Track>(Math.Min(256, cachedTracks.Count));
                for (int i = 0; i < cachedTracks.Count; i++)
                {
                    var t = cachedTracks[i];
                    if (MatchesAllTerms(t.SearchText, terms))
                    {
                        allFoundTracks.Add(t);
                    }
                }

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                var cachedArtists = _cache.CachedArtists;
                var allFoundArtists = new List<ArtistModel>(Math.Min(64, cachedArtists.Count));
                for (int i = 0; i < cachedArtists.Count; i++)
                {
                    var a = cachedArtists[i];
                    if (MatchesAllTerms(a.SearchText, terms))
                    {
                        allFoundArtists.Add(a);
                    }
                }

                if (token.IsCancellationRequested) return (new List<Track>(), new List<ArtistModel>(), new List<AlbumModel>(), 0, 0, 0);

                var cachedAlbums = _cache.CachedAlbums;
                var allFoundAlbums = new List<AlbumModel>(Math.Min(64, cachedAlbums.Count));
                for (int i = 0; i < cachedAlbums.Count; i++)
                {
                    var al = cachedAlbums[i];
                    if (MatchesAllTerms(al.SearchText, terms))
                    {
                        allFoundAlbums.Add(al);
                    }
                }

                var displayTracks = allFoundTracks.Count > 100 ? allFoundTracks.GetRange(0, 100) : allFoundTracks;
                var displayArtists = allFoundArtists.Count > 30 ? allFoundArtists.GetRange(0, 30) : allFoundArtists;
                var displayAlbums = allFoundAlbums.Count > 30 ? allFoundAlbums.GetRange(0, 30) : allFoundAlbums;

                return (displayTracks, displayArtists, displayAlbums, allFoundTracks.Count, allFoundArtists.Count, allFoundAlbums.Count);
            }, token);

            if (token.IsCancellationRequested) return;

            var app = Application.Current;
            Action updateUi = () =>
            {
                MatchingTracks.ReplaceRange(tracks);
                MatchingArtists.ReplaceRange(artists);
                MatchingAlbums.ReplaceRange(albums);

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