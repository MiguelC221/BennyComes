using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BenyComes.ViewModels;

public partial class OsuLibraryViewModel : ObservableObject
{
    private readonly OsuLibraryService _osuService = OsuLibraryService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Canciones, 1 = Artistas

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private int _totalTracksCount;

    [ObservableProperty]
    private int _totalArtistsCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ArtistModel? _selectedArtistDetail;

    [ObservableProperty]
    private AlbumModel? _selectedAlbumDetail;

    public bool IsInDetailView => SelectedArtistDetail != null || SelectedAlbumDetail != null;

    [ObservableProperty]
    private bool _isFetchingAlbumInfo;

    public ObservableCollection<string> ArtistSortOptions { get; } = new();

    [ObservableProperty]
    private string _selectedArtistSort = "Nombre (A-Z)";

    [ObservableProperty]
    private bool _isSortDescending = false;

    public string SortDirectionTooltip => IsSortDescending ? "Orden descendente (clic para ascendente)" : "Orden ascendente (clic para descendente)";
    public SymbolRegular SortDirectionIcon => IsSortDescending ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowUp24;

    public BulkObservableCollection<Track> Songs { get; } = new();
    public BulkObservableCollection<ArtistModel> Artists { get; } = new();

    // JumpList State
    [ObservableProperty]
    private bool _isJumpListOpen;

    public ObservableCollection<JumpListLetterItem> JumpListLetters { get; } = new();
    public event Action<string>? RequestScrollToLetter;

    public OsuLibraryViewModel()
    {
        ArtistSortOptions.Add("Nombre (A-Z)");
        ArtistSortOptions.Add("Cantidad de canciones");

        _osuService.LibraryUpdated += OnLibraryUpdated;
        _audio.TrackChanged += OnAudioTrackChanged;
        _audio.PlaybackStateChanged += OnAudioPlaybackStateChanged;

        RefreshData();
    }

    private void OnLibraryUpdated()
    {
        App.Current?.Dispatcher?.InvokeAsync(RefreshData);
    }

    private void OnAudioTrackChanged(object? sender, Track? current)
    {
        SyncPlayingState(current, _audio.IsPlaying);
    }

    private void OnAudioPlaybackStateChanged(object? sender, PlaybackState state)
    {
        SyncPlayingState(_audio.CurrentTrack, state == PlaybackState.Playing);
    }

    private void SyncPlayingState(Track? current, bool isPlaying)
    {
        string? currentPath = current?.FilePath;
        string? playingPath = isPlaying && current != null ? current.FilePath : null;

        void UpdateList(IEnumerable<Track> list)
        {
            foreach (var t in list)
            {
                bool isCur = currentPath != null && current != null && (t.Id == current.Id || string.Equals(t.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
                if (t.IsCurrent != isCur) t.IsCurrent = isCur;
                bool match = playingPath != null && isCur;
                if (t.IsPlaying != match) t.IsPlaying = match;
            }
        }

        UpdateList(Songs);
        if (SelectedArtistDetail?.Tracks != null) UpdateList(SelectedArtistDetail.Tracks);
        if (SelectedAlbumDetail?.Tracks != null) UpdateList(SelectedAlbumDetail.Tracks);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedArtistSortChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        IsSortDescending = !IsSortDescending;
        OnPropertyChanged(nameof(SortDirectionTooltip));
        OnPropertyChanged(nameof(SortDirectionIcon));
        ApplyFilter();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsJumpListOpen = false;
        SelectedArtistDetail = null;
        SelectedAlbumDetail = null;
        ApplyFilter();
    }

    partial void OnSelectedArtistDetailChanged(ArtistModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SelectedAlbumDetail = null;
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);

            // Fetch artist bio if missing
            if (string.IsNullOrEmpty(value.BiographySummary) && Services.Online.LastFmService.Instance.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bio = await Services.Online.LastFmService.Instance.FetchArtistBioViaApiAsync(value.Name);
                        if (!string.IsNullOrEmpty(bio.BioSummary))
                        {
                            App.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                value.BiographySummary = bio.BioSummary;
                                value.BiographyFull = bio.BioFull ?? bio.BioSummary;
                                value.HasBiography = true;
                            });
                        }
                    }
                    catch { }
                });
            }
        }
    }

    partial void OnSelectedAlbumDetailChanged(AlbumModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);
        }
    }

    public void RefreshData()
    {
        TotalTracksCount = _osuService.Tracks.Count;
        TotalArtistsCount = _osuService.Artists.Count;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchFilter?.Trim() ?? string.Empty;

        // 1. Filtrar canciones
        IEnumerable<Track> trackQuery = _osuService.Tracks;
        if (!string.IsNullOrEmpty(query))
        {
            trackQuery = trackQuery.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var trackList = trackQuery.ToList();
        Songs.ReplaceRange(trackList);
        UpdateSongsGrouping();
        SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);

        // 2. Filtrar y ordenar artistas
        IEnumerable<ArtistModel> artistQuery = _osuService.Artists;
        if (!string.IsNullOrEmpty(query))
        {
            artistQuery = artistQuery.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        bool isSortByName = string.Equals(SelectedArtistSort, "Nombre (A-Z)", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(SelectedArtistSort, "Cantidad de canciones", StringComparison.OrdinalIgnoreCase))
        {
            artistQuery = IsSortDescending
                ? artistQuery.OrderBy(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                : artistQuery.OrderByDescending(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        else // Nombre (A-Z)
        {
            artistQuery = IsSortDescending
                ? artistQuery.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                : artistQuery.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        Artists.ReplaceRange(artistQuery.ToList());
        UpdateArtistsGrouping(isSortByName && !IsSortDescending);
    }

    public void UpdateSongsGrouping()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Songs);
        if (view == null) return;

        if (view.GroupDescriptions.Count == 0)
        {
            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("AlphaGroup"));
            }
        }
    }

    public void UpdateArtistsGrouping(bool shouldGroup = true)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Artists);
        if (view == null) return;

        bool isCurrentlyGrouped = view.GroupDescriptions.Count > 0;
        if (isCurrentlyGrouped == shouldGroup) return;

        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            if (shouldGroup)
            {
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("AlphaGroup"));
            }
        }
    }

    [RelayCommand]
    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        var currentList = Songs.ToList();
        _audio.ReplaceQueueAndPlay(currentList, track, _audio.IsShuffle);
    }

    [RelayCommand]
    public void PlayAllShuffled()
    {
        var pool = Songs.ToList();
        if (pool.Count == 0) return;

        // Fisher-Yates shuffle
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        _audio.IsShuffle = true;
        _audio.PlayQueue(pool, 0);
    }

    [RelayCommand]
    public void OpenArtistDetail(ArtistModel? artist)
    {
        if (artist == null) return;
        MainViewModel.Instance?.PushCurrentNavigationState();
        SelectedArtistDetail = artist;
    }

    [RelayCommand]
    public void CloseArtistDetail()
    {
        SelectedArtistDetail = null;
    }

    [RelayCommand]
    public void CloseAlbumDetail()
    {
        SelectedAlbumDetail = null;
    }

    [RelayCommand]
    public void OpenArtistByName(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;
        var artist = Artists.FirstOrDefault(a => string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase))
                  ?? _osuService.Artists.FirstOrDefault(a => string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase));
        if (artist != null)
        {
            SelectedAlbumDetail = null;
            OpenArtistDetail(artist);
        }
    }

    [RelayCommand]
    public async Task OpenTrackAlbumAsync(Track? track)
    {
        if (track == null) return;

        MainViewModel.Instance?.PushCurrentNavigationState();
        SelectedArtistDetail = null;
        IsFetchingAlbumInfo = true;
        try
        {
            // If the track already has a fetched album name other than "osu! Beatmaps"
            string albumName = track.Album;
            string? coverUrl = null;

            if (string.Equals(albumName, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(albumName))
            {
                var info = await Services.Online.LastFmService.Instance.FetchTrackAlbumInfoAsync(track.Artist, track.Title);
                if (!string.IsNullOrWhiteSpace(info.AlbumTitle))
                {
                    albumName = info.AlbumTitle;
                    coverUrl = info.AlbumCoverUrl;
                    track.Album = albumName;
                }
                else
                {
                    albumName = track.Title;
                }
            }

            var album = new AlbumModel
            {
                Name = albumName,
                Artist = track.Artist,
                ArtworkPath = !string.IsNullOrEmpty(track.ArtworkPath) ? track.ArtworkPath : coverUrl,
                Tracks = new List<Track> { track },
                PersistedTrackCount = 1
            };

            SelectedAlbumDetail = album;
        }
        finally
        {
            IsFetchingAlbumInfo = false;
        }
    }

    [RelayCommand]
    public void OpenJumpList()
    {
        JumpListLetters.Clear();
        var activeLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedTabIndex == 0)
        {
            foreach (var s in Songs)
            {
                if (!string.IsNullOrEmpty(s.AlphaGroup))
                    activeLetters.Add(s.AlphaGroup);
            }
        }
        else if (SelectedTabIndex == 1)
        {
            foreach (var a in Artists)
            {
                if (!string.IsNullOrEmpty(a.AlphaGroup))
                    activeLetters.Add(a.AlphaGroup);
            }
        }

        foreach (var letter in Helpers.AlphaGroupHelper.Alphabet)
        {
            JumpListLetters.Add(new JumpListLetterItem
            {
                Letter = letter,
                HasItems = activeLetters.Contains(letter)
            });
        }

        IsJumpListOpen = true;
    }

    [RelayCommand]
    public void CloseJumpList()
    {
        IsJumpListOpen = false;
    }

    [RelayCommand]
    public void SelectJumpLetter(string? letter)
    {
        if (string.IsNullOrEmpty(letter)) return;
        IsJumpListOpen = false;
        RequestScrollToLetter?.Invoke(letter);
    }

    [RelayCommand]
    public void SelectTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null || artist.Tracks == null || artist.Tracks.Count == 0) return;
        _audio.PlayQueue(artist.Tracks, 0);
    }

    [RelayCommand]
    public void AddTrackToQueue(Track? track)
    {
        if (track == null) return;
        _audio.AddRangeToQueue(new[] { track }, playFirst: false);
    }

    [RelayCommand]
    public void PlayTrackNext(Track? track)
    {
        if (track == null) return;
        _audio.PlayNext(track);
    }

    [RelayCommand]
    public void AddArtistToQueue(ArtistModel? artist)
    {
        if (artist == null || artist.Tracks == null || artist.Tracks.Count == 0) return;
        _audio.AddRangeToQueue(artist.Tracks, playFirst: false);
    }
}
