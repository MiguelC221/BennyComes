using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

    public bool IsNameSortActive => string.Equals(SelectedSort, "Nombre (A-Z)", StringComparison.OrdinalIgnoreCase) ||
                                    (SelectedSort?.StartsWith("Nombre", StringComparison.OrdinalIgnoreCase) ?? false);

    [ObservableProperty]
    private bool _isSortDescending = false;

    public string SortDirectionTooltip => IsSortDescending ? "Orden descendente (clic para ascendente)" : "Orden ascendente (clic para descendente)";

    public SymbolRegular SortDirectionIcon => IsSortDescending ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowUp24;

    public ObservableCollection<string> Genres { get; } = new();

    [ObservableProperty]
    private string _selectedGenre = "Todos los géneros";

    // Collections
    public Helpers.BulkObservableCollection<Track> Songs { get; } = new();
    public Helpers.BulkObservableCollection<ArtistModel> Artists { get; } = new();
    public Helpers.BulkObservableCollection<AlbumModel> Albums { get; } = new();

    // JumpList State
    [ObservableProperty]
    private bool _isJumpListOpen;

    public ObservableCollection<JumpListLetterItem> JumpListLetters { get; } = new();
    public event Action<string>? RequestScrollToLetter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AlbumModel? _selectedAlbumDetail;

    [ObservableProperty]
    private ArtistModel? _selectedArtistDetail;

    [ObservableProperty]
    private bool _isArtistAlbumsGridView = false; // false = Vista en Lista (por álbum), true = Vista en Carátulas

    [ObservableProperty]
    private bool _isArtistHeaderCompact = false;

    [RelayCommand]
    public void SetArtistViewMode(string? mode)
    {
        IsArtistAlbumsGridView = string.Equals(mode, "Grid", StringComparison.OrdinalIgnoreCase);
    }

    // Last.fm Artist Image Sync
    [ObservableProperty]
    private bool _isLastFmArtistSyncEnabled;

    [ObservableProperty]
    private bool _isDownloadingArtistImages;

    [ObservableProperty]
    private string _artistDownloadProgressText = "Descargando imágenes de artistas...";

    private System.Threading.CancellationTokenSource? _artistDownloadCts;

    public bool IsInDetailView => SelectedAlbumDetail != null || SelectedArtistDetail != null;

    partial void OnSelectedAlbumDetailChanged(AlbumModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SyncPlayingTrack(AudioPlayerService.Instance.CurrentTrack, AudioPlayerService.Instance.IsPlaying);
        }
    }

    partial void OnSelectedArtistDetailChanged(ArtistModel? value)
    {
        IsArtistHeaderCompact = false;
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SyncPlayingTrack(AudioPlayerService.Instance.CurrentTrack, AudioPlayerService.Instance.IsPlaying);
        }
    }

    partial void OnIsLastFmArtistSyncEnabledChanged(bool value)
    {
        ArtistModel.IsLastFmArtistSyncActive = value;
        _ = Data.LibraryDao.Instance.SetSettingAsync("sync_artists_lastfm", value.ToString());

        bool isArtistsVisible = MainViewModel.Instance?.CurrentView == "MyMusic" && (SelectedTabIndex == 1 || SelectedArtistDetail != null);
        foreach (var artist in _cache.CachedArtists)
        {
            if (isArtistsVisible)
                artist.RefreshAvatar();
            else
                artist.ReleaseAvatarFromMemory();
        }

        if (value)
        {
            StartArtistImageDownloadInBackground();
        }
        else
        {
            _artistDownloadCts?.Cancel();
            IsDownloadingArtistImages = false;
        }
    }

    private void StartArtistImageDownloadInBackground()
    {
        _artistDownloadCts?.Cancel();
        _artistDownloadCts = new System.Threading.CancellationTokenSource();
        var token = _artistDownloadCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var artistsToProcess = _cache.CachedArtists
                    .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !a.Name.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (artistsToProcess.Count == 0) return;

                var pendingArtists = new List<ArtistModel>();
                foreach (var artist in artistsToProcess)
                {
                    string artistDir = Services.Online.LastFmService.Instance.GetArtistDirectory(artist.Name);
                    var localImages = Services.Online.LastFmService.Instance.GetLocalArtistImages(artistDir);
                    if (localImages.Count == 0)
                    {
                        pendingArtists.Add(artist);
                    }
                    else if (string.IsNullOrEmpty(artist.AvatarPath))
                    {
                        artist.AvatarPath = localImages[0];
                    }
                }

                if (pendingArtists.Count == 0) return;

                App.Current?.Dispatcher?.Invoke(() =>
                {
                    IsDownloadingArtistImages = true;
                    ArtistDownloadProgressText = $"Descargando imágenes de artistas (0/{pendingArtists.Count})...";
                });

                int completed = 0;
                int total = pendingArtists.Count;
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5, // Fetch up to 5 artists concurrently
                    CancellationToken = token
                };

                await Parallel.ForEachAsync(pendingArtists, parallelOptions, async (artist, ct) =>
                {
                    if (ct.IsCancellationRequested || !IsLastFmArtistSyncEnabled) return;

                    string artistName = artist.Name;
                    var thumbPath = await Services.Online.LastFmService.Instance.FetchFirstArtistThumbnailAsync(artistName, ct).ConfigureAwait(false);
                    int done = System.Threading.Interlocked.Increment(ref completed);

                    if (!string.IsNullOrEmpty(thumbPath))
                    {
                        artist.AvatarPath = thumbPath;
                    }

                    App.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        ArtistDownloadProgressText = $"Descargando imágenes de artistas ({done}/{total}): {artistName}...";

                        // Only load into memory if the user is actively viewing Artistas or this Artist's detail;
                        // otherwise keep it saved on disk and release from RAM.
                        bool shouldKeepInMemory = MainViewModel.Instance?.CurrentView == "MyMusic" &&
                                                  (SelectedTabIndex == 1 || SelectedArtistDetail == artist);
                        if (shouldKeepInMemory && !string.IsNullOrEmpty(thumbPath))
                        {
                            artist.RefreshAvatar(thumbPath);
                        }
                        else
                        {
                            artist.ReleaseAvatarFromMemory();
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                App.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    IsDownloadingArtistImages = false;
                });
            }
        }, token);
    }

    public void ReleaseUnusedThumbnailsFromMemory(bool leavingMyMusicEntirely = false)
    {
        try
        {
            bool releaseArtists = leavingMyMusicEntirely || (SelectedTabIndex != 1 && SelectedArtistDetail == null);
            bool releaseAlbums = leavingMyMusicEntirely || (SelectedTabIndex != 2 && SelectedAlbumDetail == null && SelectedArtistDetail == null);

            if (releaseArtists)
            {
                foreach (var ar in _cache.CachedArtists)
                {
                    if (ar != SelectedArtistDetail)
                        ar.ReleaseAvatarFromMemory();
                }
            }

            if (releaseAlbums)
            {
                foreach (var al in _cache.CachedAlbums)
                {
                    if (al != SelectedAlbumDetail)
                        al.ReleaseCoverFromMemory();
                }
            }

            if (leavingMyMusicEntirely)
            {
                foreach (var tr in _cache.CachedTracks)
                {
                    tr.ReleaseArtFromMemory();
                }
                Services.Metadata.MetadataService.Instance.ReleaseThumbnailsFromMemory();
            }
        }
        catch { }
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
        OnPropertyChanged(nameof(IsNameSortActive));
        if (!IsNameSortActive && IsJumpListOpen)
        {
            IsJumpListOpen = false;
        }
    }

    partial void OnSelectedTabIndexChanged(int oldValue, int newValue)
    {
        MainViewModel.Instance?.RecordTabChangeForHistory(oldValue);
        SelectedAlbumDetail = null;
        SelectedArtistDetail = null;
        UpdateSortOptions();
        OnPropertyChanged(nameof(ShuffleButtonText));
        RefreshCurrentTabData();
    }

    private async Task InitializeAsync()
    {
        await _cache.InitializeAsync();
        LoadGenresFromCache();
        RefreshData();

        string syncVal = await Data.LibraryDao.Instance.GetSettingAsync("sync_artists_lastfm", "false");
        bool isSync = bool.TryParse(syncVal, out var b) && b;
        IsLastFmArtistSyncEnabled = isSync;
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
        RefreshCurrentTabData();
    }

    [RelayCommand]
    public void RefreshData()
    {
        RefreshCurrentTabData(forceAll: true);
    }

    public void RefreshCurrentTabData(bool forceAll = false)
    {
        IsLoading = true;
        try
        {
            string sortKey = SelectedSort;
            bool isAlphaAtoZ = sortKey == "Nombre (A-Z)" && !IsSortDescending;

            if (forceAll || SelectedTabIndex == 0)
            {
                var tracks = _cache.GetFilteredTracks(sortKey, SelectedGenre, string.Empty, IsSortDescending);
                Songs.ReplaceRange(tracks);
                TotalTracksCount = tracks.Count;
            }

            if (forceAll || SelectedTabIndex == 2)
            {
                _allFilteredAlbums = _cache.GetFilteredAlbums(sortKey, SelectedGenre, string.Empty, IsSortDescending);
                var targetAlbums = isAlphaAtoZ ? _allFilteredAlbums : _allFilteredAlbums.Take(PageSize).ToList();
                Albums.ReplaceRange(targetAlbums);
                _loadedAlbumsCount = targetAlbums.Count;
            }

            if (forceAll || SelectedTabIndex == 1)
            {
                _allFilteredArtists = _cache.GetFilteredArtists(sortKey, string.Empty, IsSortDescending);
                var targetArtists = isAlphaAtoZ ? _allFilteredArtists : _allFilteredArtists.Take(PageSize).ToList();
                Artists.ReplaceRange(targetArtists);
                _loadedArtistsCount = targetArtists.Count;
            }

            UpdateGrouping();
            OnPropertyChanged(nameof(ShuffleButtonText));
            SyncPlayingTrack(AudioPlayerService.Instance.CurrentTrack, AudioPlayerService.Instance.IsPlaying);
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
        OnPropertyChanged(nameof(IsNameSortActive));
        if (!IsNameSortActive && IsJumpListOpen)
        {
            IsJumpListOpen = false;
        }
        if (_suppressFilterTriggers) return;
        RefreshCurrentTabData();
    }

    partial void OnSelectedGenreChanged(string value)
    {
        if (_suppressFilterTriggers) return;
        RefreshCurrentTabData();
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
        MainViewModel.Instance?.PushCurrentNavigationState();

        if (album.Tracks.Count == 0)
        {
            album.Tracks = _cache.CachedTracks
                .Where(t => string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        SelectedAlbumDetail = album;
    }

    [RelayCommand]
    public void CloseAlbumDetail()
    {
        var prev = SelectedAlbumDetail;
        SelectedAlbumDetail = null;
        if (SelectedTabIndex != 2)
        {
            prev?.ReleaseCoverFromMemory();
        }
    }

    [RelayCommand]
    public async Task OpenArtistDetailAsync(ArtistModel? artist)
    {
        if (artist == null) return;
        MainViewModel.Instance?.PushCurrentNavigationState();

        EnsureArtistAlbumsPopulated(artist);
        artist.RefreshAvatar();
        SelectedArtistDetail = artist;
        SyncPlayingTrack(AudioPlayerService.Instance.CurrentTrack, AudioPlayerService.Instance.IsPlaying);

        if (string.IsNullOrWhiteSpace(artist.BiographySummary))
        {
            try
            {
                string folder = Services.Online.LastFmService.SanitizeFolderName(artist.Name);
                string artistDir = Path.Combine(Services.Online.LastFmService.ArtistCacheBaseDirectory, folder);
                string bioSummaryFile = Path.Combine(artistDir, "bio_summary.txt");

                if (File.Exists(bioSummaryFile))
                {
                    artist.BiographySummary = await File.ReadAllTextAsync(bioSummaryFile);
                    string bioFullFile = Path.Combine(artistDir, "bio_full.txt");
                    if (File.Exists(bioFullFile))
                    {
                        artist.BiographyFull = await File.ReadAllTextAsync(bioFullFile);
                    }
                    artist.HasBiography = !string.IsNullOrWhiteSpace(artist.BiographySummary);
                }
                else if (IsLastFmArtistSyncEnabled && Services.Online.LastFmService.Instance.IsEnabled)
                {
                    var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(artist.Name);
                    artist.BiographySummary = info.BioSummary;
                    artist.BiographyFull = string.IsNullOrWhiteSpace(info.BioSummary) ? info.BioFull : (string.IsNullOrWhiteSpace(info.BioFull) ? info.BioSummary : info.BioFull);
                    artist.HasBiography = !string.IsNullOrWhiteSpace(artist.BiographySummary);
                    artist.RefreshAvatar();
                }
            }
            catch { }
        }
    }

    private void EnsureArtistAlbumsPopulated(ArtistModel artist)
    {
        if (artist.Tracks.Count == 0)
        {
            artist.Tracks = _cache.CachedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Artist) &&
                            t.Artist.Contains(artist.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Always build/refresh the grouped Albums list for the Artist Detail view (List grouped by album & Grid covers)
        var albumGroups = artist.Tracks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) ? "Single" : t.Album.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var orderedTracks = g
                    .OrderBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var first = orderedTracks[0];
                var existingAlbum = _cache.CachedAlbums.FirstOrDefault(a =>
                    string.Equals(a.Name, g.Key, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(a.Artist, artist.Name, StringComparison.OrdinalIgnoreCase) || a.Artist.Contains(artist.Name, StringComparison.OrdinalIgnoreCase)));

                return new AlbumModel
                {
                    Name = g.Key,
                    Artist = !string.IsNullOrWhiteSpace(first.AlbumArtist) ? first.AlbumArtist : artist.Name,
                    Year = orderedTracks.Max(t => t.Year),
                    Genre = first.Genre,
                    ArtworkPath = existingAlbum?.ArtworkPath ?? first.ArtworkPath,
                    Tracks = orderedTracks,
                    PersistedTrackCount = orderedTracks.Count
                };
            })
            .OrderByDescending(a => a.Year)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        artist.Albums = albumGroups;
    }

    public void OpenArtistDetail(ArtistModel? artist) => _ = OpenArtistDetailAsync(artist);

    [RelayCommand]
    public void CloseArtistDetail()
    {
        var prev = SelectedArtistDetail;
        SelectedArtistDetail = null;
        IsArtistHeaderCompact = false;
        if (SelectedTabIndex != 1)
        {
            prev?.ReleaseAvatarFromMemory();
        }
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

    [RelayCommand]
    public void OpenJumpList()
    {
        if (!IsNameSortActive) return;
        JumpListLetters.Clear();
        var activeLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedTabIndex == 0)
        {
            foreach (var s in Songs) activeLetters.Add(s.AlphaGroup);
        }
        else if (SelectedTabIndex == 1)
        {
            foreach (var a in Artists) activeLetters.Add(a.AlphaGroup);
        }
        else if (SelectedTabIndex == 2)
        {
            foreach (var al in Albums) activeLetters.Add(al.AlphaGroup);
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

    public void UpdateGrouping()
    {
        bool shouldGroup = SelectedSort == "Nombre (A-Z)" && !IsSortDescending;

        if (SelectedTabIndex == 0)
            ApplyGroupingToCollection(Songs, shouldGroup);
        else if (SelectedTabIndex == 1)
            ApplyGroupingToCollection(Artists, shouldGroup);
        else if (SelectedTabIndex == 2)
            ApplyGroupingToCollection(Albums, shouldGroup);
    }

    private static void ApplyGroupingToCollection(System.Collections.IEnumerable collection, bool shouldGroup)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(collection);
        if (view == null) return;

        view.GroupDescriptions.Clear();
        if (shouldGroup)
        {
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("AlphaGroup"));
        }
    }

    private Track? _lastPlayingTrack;

    public void SyncPlayingTrack(Track? currentTrack, bool isPlaying)
    {
        if (_lastPlayingTrack != null && !ReferenceEquals(_lastPlayingTrack, currentTrack))
        {
            _lastPlayingTrack.IsPlaying = false;
        }

        string? playingPath = (isPlaying && currentTrack != null) ? currentTrack.FilePath : null;

        if (currentTrack != null)
        {
            currentTrack.IsPlaying = isPlaying;
            _lastPlayingTrack = currentTrack;
        }

        foreach (var t in Songs)
        {
            bool match = playingPath != null && string.Equals(t.FilePath, playingPath, StringComparison.OrdinalIgnoreCase);
            if (t.IsPlaying != match)
            {
                t.IsPlaying = match;
            }
        }

        if (SelectedAlbumDetail != null)
        {
            foreach (var t in SelectedAlbumDetail.Tracks)
            {
                bool match = playingPath != null && string.Equals(t.FilePath, playingPath, StringComparison.OrdinalIgnoreCase);
                if (t.IsPlaying != match)
                {
                    t.IsPlaying = match;
                }
            }
        }

        if (SelectedArtistDetail != null)
        {
            foreach (var al in SelectedArtistDetail.Albums)
            {
                foreach (var t in al.Tracks)
                {
                    bool match = playingPath != null && string.Equals(t.FilePath, playingPath, StringComparison.OrdinalIgnoreCase);
                    if (t.IsPlaying != match)
                    {
                        t.IsPlaying = match;
                    }
                }
            }
        }
    }
}

public class JumpListLetterItem : ObservableObject
{
    public string Letter { get; set; } = string.Empty;
    public bool HasItems { get; set; }
}
