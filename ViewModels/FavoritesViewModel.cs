using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class FavoritesViewModel : ObservableObject
{
    private readonly Favoritos _favoritos = Favoritos.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLibraryTabContent))]
    [NotifyPropertyChangedFor(nameof(ShowOsuTabContent))]
    [NotifyPropertyChangedFor(nameof(ShowCombinedContent))]
    [NotifyPropertyChangedFor(nameof(CurrentSongsCount))]
    [NotifyPropertyChangedFor(nameof(CurrentSongsCountText))]
    private int _selectedTabIndex = 0; // 0 = Biblioteca, 1 = osu!library

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTabsBar))]
    [NotifyPropertyChangedFor(nameof(ShowLibraryTabContent))]
    [NotifyPropertyChangedFor(nameof(ShowOsuTabContent))]
    [NotifyPropertyChangedFor(nameof(ShowCombinedContent))]
    [NotifyPropertyChangedFor(nameof(CurrentSongsCount))]
    [NotifyPropertyChangedFor(nameof(CurrentSongsCountText))]
    private bool _isCombinedMode;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInDetailView))]
    private ArtistModel? _selectedArtistDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInDetailView))]
    private AlbumModel? _selectedAlbumDetail;

    public bool IsInDetailView => SelectedArtistDetail != null || SelectedAlbumDetail != null;

    public bool ShowTabsBar => !IsCombinedMode;
    public bool ShowLibraryTabContent => !IsCombinedMode && SelectedTabIndex == 0 && !IsInDetailView;
    public bool ShowOsuTabContent => !IsCombinedMode && SelectedTabIndex == 1 && !IsInDetailView;
    public bool ShowCombinedContent => IsCombinedMode && !IsInDetailView;

    public BulkObservableCollection<Track> LibrarySongs { get; } = new();
    public BulkObservableCollection<Track> OsuSongs { get; } = new();
    public BulkObservableCollection<Track> CombinedSongs { get; } = new();

    public int CurrentSongsCount => IsCombinedMode
        ? CombinedSongs.Count
        : (SelectedTabIndex == 0 ? LibrarySongs.Count : OsuSongs.Count);

    public string CurrentSongsCountText => IsCombinedMode
        ? $"{CombinedSongs.Count} canciones favoritas (combinadas)"
        : (SelectedTabIndex == 0
            ? $"{LibrarySongs.Count} favoritas en Biblioteca"
            : $"{OsuSongs.Count} favoritas en osu!library");

    // JumpList State
    [ObservableProperty]
    private bool _isJumpListOpen;

    public ObservableCollection<JumpListLetterItem> JumpListLetters { get; } = new();
    public event Action<string>? RequestScrollToLetter;

    public FavoritesViewModel()
    {
        _isCombinedMode = _favoritos.CombineLibraries;

        _favoritos.FavoritesUpdated += OnFavoritesUpdated;
        _audio.TrackChanged += OnAudioTrackChanged;
        _audio.PlaybackStateChanged += OnAudioPlaybackStateChanged;

        RefreshData();
    }

    private void OnFavoritesUpdated()
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

        UpdateList(LibrarySongs);
        UpdateList(OsuSongs);
        UpdateList(CombinedSongs);
        if (SelectedArtistDetail?.Tracks != null) UpdateList(SelectedArtistDetail.Tracks);
        if (SelectedAlbumDetail?.Tracks != null) UpdateList(SelectedAlbumDetail.Tracks);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsJumpListOpen = false;
        SelectedArtistDetail = null;
        SelectedAlbumDetail = null;
        OnPropertyChanged(nameof(ShowLibraryTabContent));
        OnPropertyChanged(nameof(ShowOsuTabContent));
        OnPropertyChanged(nameof(ShowCombinedContent));
        OnPropertyChanged(nameof(CurrentSongsCount));
        OnPropertyChanged(nameof(CurrentSongsCountText));
    }

    partial void OnIsCombinedModeChanged(bool value)
    {
        IsJumpListOpen = false;
        SelectedArtistDetail = null;
        SelectedAlbumDetail = null;
        _ = _favoritos.SetCombineLibrariesAsync(value);
        OnPropertyChanged(nameof(ShowTabsBar));
        OnPropertyChanged(nameof(ShowLibraryTabContent));
        OnPropertyChanged(nameof(ShowOsuTabContent));
        OnPropertyChanged(nameof(ShowCombinedContent));
        OnPropertyChanged(nameof(CurrentSongsCount));
        OnPropertyChanged(nameof(CurrentSongsCountText));
    }

    partial void OnSelectedArtistDetailChanged(ArtistModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        OnPropertyChanged(nameof(ShowLibraryTabContent));
        OnPropertyChanged(nameof(ShowOsuTabContent));
        OnPropertyChanged(nameof(ShowCombinedContent));

        if (value != null)
        {
            SelectedAlbumDetail = null;
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);

            if (string.IsNullOrEmpty(value.BiographySummary) && Services.Online.LastFmService.Instance.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bio = await Services.Online.LastFmService.Instance.FetchArtistBioViaApiAsync(value.Name);
                        if (string.IsNullOrEmpty(bio.BioSummary) && value.AltNames.Count > 0)
                        {
                            foreach (var alt in value.AltNames)
                            {
                                bio = await Services.Online.LastFmService.Instance.FetchArtistBioViaApiAsync(alt);
                                if (!string.IsNullOrEmpty(bio.BioSummary)) break;
                            }
                        }
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
        OnPropertyChanged(nameof(ShowLibraryTabContent));
        OnPropertyChanged(nameof(ShowOsuTabContent));
        OnPropertyChanged(nameof(ShowCombinedContent));

        if (value != null)
        {
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);
        }
    }

    public void RefreshData()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchFilter?.Trim() ?? string.Empty;

        static bool MatchesTrack(Track t, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            return t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(t.Album) && t.Album.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrEmpty(t.OsuArtistRoumaji) && t.OsuArtistRoumaji.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrEmpty(t.OsuArtistUnicode) && t.OsuArtistUnicode.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var libList = _favoritos.GetLibraryFavoriteTracks().Where(t => MatchesTrack(t, query)).ToList();
        var osuList = _favoritos.GetOsuFavoriteTracks().Where(t => MatchesTrack(t, query)).ToList();
        var allList = _favoritos.GetAllFavoriteTracks().Where(t => MatchesTrack(t, query)).ToList();

        LibrarySongs.ReplaceRange(libList);
        OsuSongs.ReplaceRange(osuList);
        CombinedSongs.ReplaceRange(allList);

        EnsureGrouping(LibrarySongs);
        EnsureGrouping(OsuSongs);
        EnsureGrouping(CombinedSongs);

        SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);

        OnPropertyChanged(nameof(CurrentSongsCount));
        OnPropertyChanged(nameof(CurrentSongsCountText));
    }

    private static void EnsureGrouping(BulkObservableCollection<Track> collection)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(collection);
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

    public BulkObservableCollection<Track> GetActiveSongsCollection()
    {
        if (IsCombinedMode) return CombinedSongs;
        return SelectedTabIndex == 0 ? LibrarySongs : OsuSongs;
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
    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        var currentList = GetActiveSongsCollection().ToList();
        if (currentList.Count == 0) currentList.Add(track);
        _audio.ReplaceQueueAndPlay(currentList, track, _audio.IsShuffle);
    }

    [RelayCommand]
    public void PlayAllShuffled()
    {
        var pool = GetActiveSongsCollection().ToList();
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
    public void PlayTrackNext(Track? track)
    {
        if (track == null) return;
        _audio.PlayNext(track);
    }

    [RelayCommand]
    public void AddTrackToQueue(Track? track)
    {
        if (track == null) return;
        _audio.AddRangeToQueue(new[] { track }, playFirst: false);
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null || artist.Tracks == null || artist.Tracks.Count == 0) return;
        _audio.PlayQueue(artist.Tracks, 0);
    }

    [RelayCommand]
    public void OpenArtistByName(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;

        bool MatchesArtist(ArtistModel a) =>
            string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase) ||
            a.AltNames.Any(alt => string.Equals(alt, artistName, StringComparison.OrdinalIgnoreCase));

        // Buscar primero en artistas de osu! o de la biblioteca normal
        var artist = OsuLibraryService.Instance.Artists.FirstOrDefault(MatchesArtist)
                  ?? LibraryCacheService.Instance.CachedArtists.FirstOrDefault(MatchesArtist);

        if (artist == null)
        {
            var tracks = GetActiveSongsCollection()
                .Where(t => string.Equals(t.Artist, artistName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            artist = new ArtistModel
            {
                Name = artistName.Trim(),
                Tracks = tracks,
                PersistedTrackCount = tracks.Count,
                AvatarPath = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ArtworkPath))?.ArtworkPath
            };
        }

        MainViewModel.Instance?.PushCurrentNavigationState();
        SelectedAlbumDetail = null;
        SelectedArtistDetail = artist;
    }

    [RelayCommand]
    public async Task OpenTrackAlbumAsync(Track? track)
    {
        if (track == null) return;

        MainViewModel.Instance?.PushCurrentNavigationState();
        SelectedArtistDetail = null;

        string albumName = track.Album;
        string? coverUrl = null;

        if (string.IsNullOrWhiteSpace(albumName) ||
            string.Equals(albumName, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(albumName, "Unknown Album", StringComparison.OrdinalIgnoreCase))
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

        // Si el álbum existe en la biblioteca normal, traer sus canciones
        var cachedAlbum = LibraryCacheService.Instance.CachedAlbums
            .FirstOrDefault(a => string.Equals(a.Name, albumName, StringComparison.OrdinalIgnoreCase));

        List<Track> albumTracks;
        if (cachedAlbum != null && cachedAlbum.Tracks.Count > 0)
        {
            albumTracks = cachedAlbum.Tracks;
        }
        else
        {
            albumTracks = new List<Track> { track };
        }

        SelectedAlbumDetail = new AlbumModel
        {
            Name = albumName,
            Artist = track.Artist,
            ArtworkPath = !string.IsNullOrEmpty(track.ArtworkPath) ? track.ArtworkPath : (coverUrl ?? cachedAlbum?.ArtworkPath),
            Tracks = albumTracks,
            PersistedTrackCount = albumTracks.Count
        };
    }

    [RelayCommand]
    public void OpenJumpList()
    {
        JumpListLetters.Clear();
        var activeLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in GetActiveSongsCollection())
        {
            if (!string.IsNullOrEmpty(s.AlphaGroup))
                activeLetters.Add(s.AlphaGroup);
        }

        foreach (var letter in AlphaGroupHelper.Alphabet)
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
}
