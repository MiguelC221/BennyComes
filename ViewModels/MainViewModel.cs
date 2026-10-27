using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BenyComes.Data;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    private readonly LibraryDao _libraryDao = LibraryDao.Instance;
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;

    public HomeViewModel Home { get; }
    public NowPlayingViewModel NowPlaying { get; }
    public QueueViewModel Queue { get; }
    public PlaylistsViewModel Playlists { get; }
    public AudioDeviceSelectorViewModel DeviceSelector { get; }
    public MyMusicViewModel MyMusic { get; }
    public RecentPlaysViewModel RecentPlays { get; }
    public SettingsViewModel Settings { get; }
    public SearchViewModel Search { get; }
    public ThemeService Theme => ThemeService.Instance;
    public EqualizerService Equalizer => EqualizerService.Instance;

    [ObservableProperty]
    private string _currentView = "Home"; // Home, MyMusic, RecentPlays, Search, Settings, PlaylistDetail

    [ObservableProperty]
    private bool _isNowPlayingActive = false;

    [ObservableProperty]
    private bool _isNowPlayingQueueExpanded = false;

    [ObservableProperty]
    private bool _isEqualizerOpen = false;

    public Track? CurrentTrack => _audio?.CurrentTrack ?? NowPlaying?.CurrentTrack;

    [ObservableProperty]
    private Playlist? _selectedPlaylistForDetail;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isNavPaneOpen = true;

    [ObservableProperty]
    private bool _isQueueDrawerOpen;

    [ObservableProperty]
    private bool _isPlaylistsDrawerOpen;

    [ObservableProperty]
    private bool _isDeviceSelectorOpen;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private string _notificationTitle = string.Empty;

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private bool _isNotificationVisible;

    [ObservableProperty]
    private bool _isNotificationWarning;

    // Onboarding / First-run welcome state
    [ObservableProperty]
    private bool _isOnboardingVisible;

    public ObservableCollection<string> OnboardingFolders { get; } = new();

    // Async Scan & Indexing Overlay State
    [ObservableProperty]
    private bool _isScanningOverlayVisible;

    [ObservableProperty]
    private string _scanOverlayTitle = "Sincronizando tu biblioteca musical y carátulas...";

    [ObservableProperty]
    private string _scanOverlayCountText = "Preparando...";

    [ObservableProperty]
    private string _scanOverlayCurrentFile = string.Empty;

    [ObservableProperty]
    private double _scanOverlayProgress = 0.0;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isShuffleEnabled;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private bool _isMuted;

    private readonly DispatcherTimer _playbackTimer;

    public static MainViewModel? Instance { get; private set; }

    public MainViewModel()
    {
        Instance = this;
        Home = new HomeViewModel();
        NowPlaying = new NowPlayingViewModel();
        Queue = new QueueViewModel();
        Playlists = new PlaylistsViewModel();
        DeviceSelector = new AudioDeviceSelectorViewModel();
        MyMusic = new MyMusicViewModel();
        RecentPlays = new RecentPlaysViewModel();
        Settings = new SettingsViewModel();
        Search = new SearchViewModel();

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _playbackTimer.Tick += (s, e) => UpdatePlaybackPosition();

        IsShuffleEnabled = _audio.IsShuffle;
        NowPlaying.IsShuffleEnabled = _audio.IsShuffle;
        RepeatMode = _audio.RepeatMode;
        NowPlaying.RepeatMode = _audio.RepeatMode;
        IsMuted = _audio.IsMuted;
        NowPlaying.IsMuted = _audio.IsMuted;

        _audio.NotificationRaised += OnNotificationRaised;

        _audio.ShuffleChanged += (s, shuffle) =>
        {
            IsShuffleEnabled = shuffle;
            NowPlaying.IsShuffleEnabled = shuffle;
        };

        _audio.RepeatModeChanged += (s, mode) =>
        {
            RepeatMode = mode;
            NowPlaying.RepeatMode = mode;
        };

        _audio.MuteChanged += (s, muted) =>
        {
            IsMuted = muted;
            NowPlaying.IsMuted = muted;
        };

        Settings.OnLibraryUpdated += async () =>
        {
            await MyMusic.RefreshDataAsync();
            await RecentPlays.LoadRecentAsync();
            await Home.LoadHomeDataAsync();
            Search.RefreshResults();
        };

        Settings.OnScanRequested += async (folders) =>
        {
            await ScanDirectoriesOrFilesAsync(folders);
        };

        _audio.TrackChanged += (s, t) =>
        {
            if (t != null)
            {
                ForceUiPlaybackUpdate(t, _audio.IsPlaying);
            }
            _ = SavePlaybackStateAsync();
        };

        _audio.PlaybackStateChanged += (s, state) =>
        {
            bool playing = state == PlaybackState.Playing;
            ForceUiPlaybackUpdate(_audio.CurrentTrack, playing);
        };

        _audio.OnPlaybackStateChanged += (track, isPlaying) =>
        {
            ForceUiPlaybackUpdate(track ?? _audio.CurrentTrack, isPlaying);
        };

        _ = InitializeDataAsync();
    }

    public void UpdatePlaybackPosition()
    {
        if (!NowPlaying.IsUserSeeking)
        {
            var pos = _audio.CurrentTime;
            var total = _audio.TotalTime;
            NowPlaying.CurrentPositionSeconds = pos.TotalSeconds;
            NowPlaying.CurrentTimeText = pos.Hours > 0
                ? pos.ToString(@"h\:mm\:ss")
                : pos.ToString(@"m\:ss");

            if (total.TotalSeconds > 0)
            {
                NowPlaying.TotalDurationSeconds = total.TotalSeconds;
                NowPlaying.TotalTimeText = total.Hours > 0
                    ? total.ToString(@"h\:mm\:ss")
                    : total.ToString(@"m\:ss");
            }
        }
    }

    private record struct NavigationStateEntry(
        string ViewName,
        int MyMusicTabIndex,
        ArtistModel? ArtistDetail,
        AlbumModel? AlbumDetail,
        Playlist? PlaylistDetail
    );

    private readonly Stack<NavigationStateEntry> _navigationHistory = new();
    private bool _isRestoringNavigation;
    private Track? _lastHighResArtTrack;

    [ObservableProperty]
    private bool _canGoBack;

    public void PushCurrentNavigationState()
    {
        if (_isRestoringNavigation) return;

        var entry = new NavigationStateEntry(
            CurrentView,
            MyMusic.SelectedTabIndex,
            MyMusic.SelectedArtistDetail,
            MyMusic.SelectedAlbumDetail,
            SelectedPlaylistForDetail
        );

        if (_navigationHistory.Count == 0 || !_navigationHistory.Peek().Equals(entry))
        {
            _navigationHistory.Push(entry);
        }
        UpdateCanGoBack();
    }

    public void RecordTabChangeForHistory(int oldTabIndex)
    {
        if (_isRestoringNavigation || CurrentView != "MyMusic") return;

        var entry = new NavigationStateEntry(
            "MyMusic",
            oldTabIndex,
            MyMusic.SelectedArtistDetail,
            MyMusic.SelectedAlbumDetail,
            SelectedPlaylistForDetail
        );

        if (_navigationHistory.Count == 0 || !_navigationHistory.Peek().Equals(entry))
        {
            _navigationHistory.Push(entry);
        }
        UpdateCanGoBack();
    }

    private void ClearNavigationHistory()
    {
        _navigationHistory.Clear();
        CanGoBack = false;
    }

    private void UpdateCanGoBack()
    {
        if (CurrentView == "Home")
        {
            _navigationHistory.Clear();
            CanGoBack = false;
        }
        else
        {
            CanGoBack = _navigationHistory.Count > 0;
        }
    }

    [RelayCommand]
    public void GoBack()
    {
        if (_navigationHistory.Count == 0 || CurrentView == "Home")
        {
            ClearNavigationHistory();
            return;
        }

        _isRestoringNavigation = true;
        try
        {
            var prev = _navigationHistory.Pop();
            IsNowPlayingActive = false;

            if (prev.ViewName == "Home")
            {
                CurrentView = "Home";
                MyMusic.SelectedAlbumDetail = null;
                MyMusic.SelectedArtistDetail = null;
                MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: true);
                ClearNavigationHistory();
                _ = Home.LoadHomeDataAsync();
                return;
            }

            bool wasMyMusic = CurrentView == "MyMusic";
            CurrentView = prev.ViewName;

            if (prev.ViewName == "MyMusic")
            {
                MyMusic.SelectedTabIndex = prev.MyMusicTabIndex;
                MyMusic.SelectedArtistDetail = prev.ArtistDetail;
                MyMusic.SelectedAlbumDetail = prev.AlbumDetail;
                MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: false);
            }
            else
            {
                if (wasMyMusic)
                {
                    MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: true);
                }

                if (prev.ViewName == "PlaylistDetail" && prev.PlaylistDetail != null)
                {
                    SelectedPlaylistForDetail = prev.PlaylistDetail;
                }
            }

            UpdateCanGoBack();
        }
        finally
        {
            _isRestoringNavigation = false;
        }
    }

    public void ForceUiPlaybackUpdate(Track? currentTrack, bool isPlaying)
    {
        var app = Application.Current;
        if (app == null) return;

        Action update = () =>
        {
            if (currentTrack != null)
            {
                bool trackChanged = !ReferenceEquals(_lastHighResArtTrack, currentTrack);
                if (trackChanged && _lastHighResArtTrack != null)
                {
                    // Release previous track's high-res artwork from memory as soon as it finishes/changes
                    _lastHighResArtTrack.ReleaseArtFromMemory();
                    NowPlaying.AlbumArt = null;
                    NowPlaying.BackgroundImage = null;
                }
                _lastHighResArtTrack = currentTrack;

                NowPlaying.CurrentTrack = currentTrack;
                OnPropertyChanged(nameof(CurrentTrack));
                NowPlaying.Title = string.IsNullOrWhiteSpace(currentTrack.Title) ? "Canción desconocida" : currentTrack.Title;
                NowPlaying.Artist = string.IsNullOrWhiteSpace(currentTrack.Artist) ? "Artista desconocido" : currentTrack.Artist;
                NowPlaying.Album = currentTrack.Album ?? "";
                NowPlaying.ArtistAndAlbum = currentTrack.ArtistAndAlbum;

                if (trackChanged || NowPlaying.AlbumArt == null)
                {
                    var art = MetadataService.Instance.GetHighResAlbumArt(currentTrack);
                    NowPlaying.AlbumArt = art;
                    NowPlaying.BackgroundImage = MetadataService.Instance.GetBlurredBackgroundArt(currentTrack);
                    ThemeService.Instance.OnTrackArtChanged(art);
                }

                NowPlaying.TotalDurationSeconds = Math.Max(1, currentTrack.Duration.TotalSeconds);
                NowPlaying.TotalTimeText = currentTrack.DurationFormatted;
            }

            IsPlaying = isPlaying;
            NowPlaying.IsPlaying = isPlaying;
            MyMusic.SyncPlayingTrack(currentTrack, isPlaying);
            if (SelectedPlaylistForDetail != null)
            {
                foreach (var t in SelectedPlaylistForDetail.Tracks)
                {
                    bool shouldPlay = isPlaying && currentTrack != null && (t.Id == currentTrack.Id || t.FilePath == currentTrack.FilePath);
                    if (t.IsPlaying != shouldPlay)
                    {
                        t.IsPlaying = shouldPlay;
                    }
                }
            }

            if (isPlaying)
            {
                if (!_playbackTimer.IsEnabled)
                    _playbackTimer.Start();
                UpdatePlaybackPosition();
            }
            else
            {
                _playbackTimer.Stop();
            }
        };

        if (app.Dispatcher.CheckAccess())
            update();
        else
            app.Dispatcher.InvokeAsync(update);
    }

    private void OnNotificationRaised(object? sender, (string Title, string Message, bool IsWarning) e)
    {
        NotificationTitle = e.Title;
        NotificationMessage = e.Message;
        IsNotificationWarning = e.IsWarning;
        IsNotificationVisible = true;
    }

    [RelayCommand]
    private void DismissNotification()
    {
        IsNotificationVisible = false;
    }

    [RelayCommand]
    public void NavigateTo(string viewName)
    {
        if (viewName == "NowPlaying")
        {
            OpenNowPlaying();
            return;
        }

        if (viewName == "Home")
        {
            // Rule: Navigation history ALWAYS clears when clicking or arriving at Inicio (Home)
            ClearNavigationHistory();
        }
        else if (CurrentView != viewName || MyMusic.IsInDetailView)
        {
            PushCurrentNavigationState();
        }

        bool leavingMyMusic = CurrentView == "MyMusic" && viewName != "MyMusic";

        IsNowPlayingActive = false;
        CurrentView = viewName;
        UpdateCanGoBack();

        if (leavingMyMusic)
        {
            MyMusic.SelectedAlbumDetail = null;
            MyMusic.SelectedArtistDetail = null;
            MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: true);
        }

        if (viewName == "Home")
        {
            ClearNavigationHistory();
            _ = Home.LoadHomeDataAsync();
        }
        else if (viewName == "MyMusic")
        {
            MyMusic.SelectedAlbumDetail = null;
            MyMusic.SelectedArtistDetail = null;
            _ = MyMusic.RefreshDataAsync();
        }
        else if (viewName == "RecentPlays")
        {
            _ = RecentPlays.LoadRecentAsync();
        }
        else if (viewName == "Search")
        {
            Search.RefreshResults();
        }
        else if (viewName == "Settings")
        {
            _ = Settings.LoadFoldersAsync();
        }
    }

    [RelayCommand]
    public void OpenNowPlaying()
    {
        IsNowPlayingActive = true;
    }

    [RelayCommand]
    public void CloseNowPlaying()
    {
        IsNowPlayingActive = false;
        IsNowPlayingQueueExpanded = false;
    }

    [RelayCommand]
    public void ToggleNowPlayingQueue()
    {
        IsNowPlayingQueueExpanded = !IsNowPlayingQueueExpanded;
    }

    [RelayCommand]
    public void ToggleNavPane()
    {
        IsNavPaneOpen = !IsNavPaneOpen;
    }

    [RelayCommand]
    public async Task OpenPlaylistDetailAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        PushCurrentNavigationState();

        if (CurrentView == "MyMusic")
        {
            MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: true);
        }

        SelectedPlaylistForDetail = playlist;

        var tracks = await PlaylistDao.Instance.GetPlaylistTracksAsync(playlist.Id);
        playlist.Tracks.Clear();
        foreach (var t in tracks)
        {
            t.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(t);
            playlist.Tracks.Add(t);
        }
        playlist.TrackCount = playlist.Tracks.Count;
        playlist.RefreshCoverImage();

        LoadSuggestedTracks(playlist);

        CurrentView = "PlaylistDetail";
        IsNowPlayingActive = false;
        UpdateCanGoBack();
    }

    [RelayCommand]
    public void CreatePlaylistQuick(Track? track)
    {
        OpenCreatePlaylistDialog(track);
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (Search.SearchQuery != value)
        {
            Search.SearchQuery = value;
        }
    }

    [RelayCommand]
    public void ExecuteSearchFromHeader()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        if (CurrentView != "Search")
        {
            PushCurrentNavigationState();
            if (CurrentView == "MyMusic")
            {
                MyMusic.ReleaseUnusedThumbnailsFromMemory(leavingMyMusicEntirely: true);
            }
            CurrentView = "Search";
            IsNowPlayingActive = false;
            UpdateCanGoBack();
        }
        _ = Search.ExecuteFullSearchAsync();
    }

    [RelayCommand]
    private void ToggleQueueDrawer()
    {
        IsQueueDrawerOpen = !IsQueueDrawerOpen;
        if (IsQueueDrawerOpen)
        {
            IsPlaylistsDrawerOpen = false;
            IsDeviceSelectorOpen = false;
        }
    }

    [RelayCommand]
    private void TogglePlaylistsDrawer()
    {
        IsPlaylistsDrawerOpen = !IsPlaylistsDrawerOpen;
        if (IsPlaylistsDrawerOpen)
        {
            IsQueueDrawerOpen = false;
            IsDeviceSelectorOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleDeviceSelector()
    {
        IsDeviceSelectorOpen = !IsDeviceSelectorOpen;
        if (IsDeviceSelectorOpen)
        {
            DeviceSelector.RefreshDevices();
            IsQueueDrawerOpen = false;
            IsPlaylistsDrawerOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleFullscreen(Window? window = null)
    {
        window ??= Application.Current?.MainWindow;
        if (window == null) return;

        IsFullscreen = !IsFullscreen;
        if (IsFullscreen)
        {
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Maximized;
        }
        else
        {
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.WindowState = WindowState.Normal;
        }
    }

    public async Task HandleFilesDroppedAsync(string[] filePaths)
    {
        await ScanDirectoriesOrFilesAsync(filePaths);
    }

    public async Task ScanDirectoriesOrFilesAsync(IEnumerable<string> paths)
    {
        var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".flac", ".ogg", ".wav", ".mp3", ".m4a", ".aac", ".opus", ".wma"
        };

        var filesToProcess = await Task.Run(() =>
        {
            var list = new List<string>(512);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            foreach (var path in paths)
            {
                if (File.Exists(path) && extSet.Contains(Path.GetExtension(path)))
                {
                    if (seen.Add(path)) list.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(path, "*.*", enumOptions))
                        {
                            if (extSet.Contains(Path.GetExtension(file)) && seen.Add(file))
                            {
                                list.Add(file);
                            }
                        }
                    }
                    catch { }
                }
            }
            return list;
        });

        if (filesToProcess.Count == 0) return;

        IsScanningOverlayVisible = true;
        ScanOverlayTitle = "Indexando música, álbumes, artistas y buscador en AppData...";
        ScanOverlayCountText = $"Preparando {filesToProcess.Count} pistas...";
        ScanOverlayCurrentFile = "";
        ScanOverlayProgress = 0.0;

        var allScannedTracks = new List<Track>(filesToProcess.Count);

        await Task.Run(async () =>
        {
            LibraryIndexDatabase.Instance.WarmupArtworkDirectoryCache();

            // Pre-map already indexed tracks in memory to skip unchanged files instantly
            var existingMap = new Dictionary<string, Track>(_cache.CachedTracks.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var ct in _cache.CachedTracks)
            {
                if (!string.IsNullOrEmpty(ct.FilePath))
                    existingMap[ct.FilePath] = ct;
            }

            var folderArtCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var progress = new Progress<ScanProgressReport>(r =>
            {
                ScanOverlayCountText = r.StatusText;
                ScanOverlayCurrentFile = r.CurrentFile;
                ScanOverlayProgress = r.ProgressPercentage;
            });

            int total = filesToProcess.Count;
            int processedCount = 0;
            const int batchSize = 60;
            var chunks = filesToProcess.Chunk(batchSize).ToList();
            int maxDegree = Math.Clamp(Environment.ProcessorCount, 2, 6);

            for (int cIdx = 0; cIdx < chunks.Count; cIdx++)
            {
                var chunk = chunks[cIdx];
                bool isFinalBatch = (cIdx == chunks.Count - 1);
                var chunkResults = new Track[chunk.Length];
                var changedMask = new bool[chunk.Length];

                Parallel.For(0, chunk.Length, new ParallelOptions { MaxDegreeOfParallelism = maxDegree }, i =>
                {
                    string file = chunk[i];
                    Track? track = null;
                    bool isUnchanged = false;

                    if (existingMap.TryGetValue(file, out var cachedTrack) && cachedTrack.Id > 0)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Exists &&
                                Math.Abs((fi.LastWriteTimeUtc - cachedTrack.DateModified).TotalSeconds) < 2.0 &&
                                (cachedTrack.FileSize == 0 || cachedTrack.FileSize == fi.Length))
                            {
                                track = cachedTrack;
                                isUnchanged = true;
                            }
                        }
                        catch { }
                    }

                    if (track == null)
                    {
                        track = MetadataService.Instance.ExtractTrackMetadataFast(file, folderArtCache);
                        if (cachedTrack != null)
                        {
                            track.Id = cachedTrack.Id;
                            track.IsFavorite = cachedTrack.IsFavorite;
                            track.PlayCount = cachedTrack.PlayCount;
                        }
                    }

                    chunkResults[i] = track;
                    changedMask[i] = !isUnchanged;

                    int cur = System.Threading.Interlocked.Increment(ref processedCount);
                    if (cur % 3 == 0 || cur == total)
                    {
                        ((IProgress<ScanProgressReport>)progress).Report(new ScanProgressReport
                        {
                            Current = cur,
                            Total = total,
                            CurrentFile = Path.GetFileName(file)
                        });
                    }
                });

                var batchTracksToPersist = new List<Track>(chunk.Length);
                var batchAllTracks = new List<Track>(chunk.Length);

                for (int i = 0; i < chunkResults.Length; i++)
                {
                    var tr = chunkResults[i];
                    if (tr != null)
                    {
                        batchAllTracks.Add(tr);
                        allScannedTracks.Add(tr);
                        if (changedMask[i])
                        {
                            batchTracksToPersist.Add(tr);
                        }
                    }
                }

                // 1. Persist new/modified tracks to primary SQLite DB in batch transaction (populates track.Id)
                if (batchTracksToPersist.Count > 0)
                {
                    await _libraryDao.InsertOrUpdateTracksBatchAsync(batchTracksToPersist);
                }

                // 2. Progressively update partitioned AppData IndexDB (Songs, Albums, Artists, Search) while indexing!
                await _cache.IntegrateIndexedBatchAsync(batchAllTracks, isFinalBatch);
            }
        });

        if (_audio.Queue.Count == 0 && allScannedTracks.Count > 0)
        {
            foreach (var t in allScannedTracks.Take(50))
            {
                _audio.AddToQueue(t, playImmediately: false);
            }
        }
        else
        {
            var existingQueuePaths = _audio.Queue.Select(q => q.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in allScannedTracks)
            {
                if (!existingQueuePaths.Contains(t.FilePath) && _audio.Queue.Count < 200)
                {
                    _audio.AddToQueue(t, playImmediately: false);
                }
            }
        }

        await MyMusic.RefreshDataAsync();
        await RecentPlays.LoadRecentAsync();
        await Home.LoadHomeDataAsync();

        IsScanningOverlayVisible = false;

        // Auto-enqueue pending artists for background enrichment
        _ = Services.Online.ArtistEnrichmentService.Instance.EnqueueAndStartAsync();
    }

    [RelayCommand]
    public async Task GenerateDemoTracksAsync()
    {
        try
        {
            var track1 = await DemoAudioGenerator.GenerateHiResSampleTrackAsync(
                "BenyComes_StudioMaster_96k24b.wav",
                "Beny High-Fidelity Resonance (Studio Master)",
                "BenyComes Acoustic Lab",
                "Hi-Res Reference Vol. 1",
                sampleRate: 96000,
                bitsPerSample: 24,
                durationSecs: 45.0
            );

            var track2 = await DemoAudioGenerator.GenerateHiResSampleTrackAsync(
                "Acoustic_Horizon_44k16b.wav",
                "Acoustic Horizon (CD Bit-Perfect)",
                "BenyComes Acoustic Lab",
                "Hi-Res Reference Vol. 1",
                sampleRate: 44100,
                bitsPerSample: 16,
                durationSecs: 40.0
            );

            await _libraryDao.InsertOrUpdateTrackAsync(track1);
            await _libraryDao.InsertOrUpdateTrackAsync(track2);

            _audio.AddToQueue(track1, playImmediately: _audio.Queue.Count == 0);
            _audio.AddToQueue(track2, playImmediately: false);

            await MyMusic.RefreshDataAsync();

            OnNotificationRaised(this, (
                "Pistas Hi-Fi Generadas",
                "Se han añadido 2 pistas de demostración (24-bit 96 kHz Studio Master y 16-bit 44.1 kHz).",
                false
            ));
        }
        catch (Exception ex)
        {
            OnNotificationRaised(this, ("Error", ex.Message, true));
        }
    }

    public async Task SavePlaybackStateAsync()
    {
        try
        {
            string lastFile = _audio.CurrentTrack?.FilePath ?? "";
            double lastPos = _audio.CurrentPosition.TotalSeconds;
            string queuePaths = string.Join('|', _audio.Queue.Select(t => t.FilePath));

            await _libraryDao.SetSettingAsync("last_played_file", lastFile);
            await _libraryDao.SetSettingAsync("last_played_position", lastPos.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await _libraryDao.SetSettingAsync("last_queue_files", queuePaths);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save playback state: {ex.Message}");
        }
    }

    private async Task InitializeDataAsync()
    {
        // 1. Load pre-indexed Songs, Albums, Artists, and Search instantaneously from %LocalAppData%\BenyComes\IndexDB\
        await _cache.InitializeAsync();

        // 2. Populate UI views immediately so Artists, Albums, Songs, and Home appear with 0ms perceived latency
        await MyMusic.RefreshDataAsync();
        _ = RecentPlays.LoadRecentAsync();
        _ = Home.LoadHomeDataAsync();

        var existing = _cache.CachedTracks.ToList();

        // Limpieza de pistas de demostración para mantener la biblioteca 100% limpia
        try
        {
            var demoTracks = existing.Where(t =>
                t.FilePath.Contains("DemoTracks", StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains("BenyComes Acoustic Lab", StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains("Hi-Res Reference", StringComparison.OrdinalIgnoreCase)).ToList();

            if (demoTracks.Count > 0)
            {
                foreach (var dt in demoTracks)
                {
                    await _libraryDao.DeleteTrackAsync(dt.Id);
                    try { if (File.Exists(dt.FilePath)) File.Delete(dt.FilePath); } catch { }
                }
                existing.RemoveAll(t => demoTracks.Contains(t));
                await _cache.ReloadFromDatabaseAsync();
                await MyMusic.RefreshDataAsync();
                await Home.LoadHomeDataAsync();
            }
        }
        catch { }

        string lastFile = await _libraryDao.GetSettingAsync("last_played_file", "");
        string posStr = await _libraryDao.GetSettingAsync("last_played_position", "0");
        string queueStr = await _libraryDao.GetSettingAsync("last_queue_files", "");
        double.TryParse(posStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lastSecs);

        if (!string.IsNullOrWhiteSpace(queueStr))
        {
            var paths = queueStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var trackMap = new Dictionary<string, Track>(existing.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var tr in existing)
            {
                if (!string.IsNullOrEmpty(tr.FilePath))
                    trackMap[tr.FilePath] = tr;
            }

            foreach (var p in paths)
            {
                if (trackMap.TryGetValue(p, out var t))
                {
                    _audio.AddToQueue(t, playImmediately: false);
                }
                else if (File.Exists(p))
                {
                    var loaded = await MetadataService.Instance.LoadTrackMetadataAsync(p);
                    _audio.AddToQueue(loaded, playImmediately: false);
                }
            }
        }
        else if (existing.Count > 0)
        {
            foreach (var t in existing.Take(25))
            {
                _audio.AddToQueue(t, playImmediately: false);
            }
        }

        // Check if there is a last played track to resume
        Track? targetTrack = null;
        if (!string.IsNullOrWhiteSpace(lastFile))
        {
            targetTrack = _audio.Queue.FirstOrDefault(t => t.FilePath.Equals(lastFile, StringComparison.OrdinalIgnoreCase));
            if (targetTrack == null && File.Exists(lastFile))
            {
                targetTrack = await MetadataService.Instance.LoadTrackMetadataAsync(lastFile);
                _audio.AddToQueue(targetTrack, playImmediately: false);
            }
        }

        if (targetTrack == null && _audio.Queue.Count > 0)
        {
            targetTrack = _audio.Queue[0];
            lastSecs = 0;
        }

        if (targetTrack != null)
        {
            int idx = _audio.Queue.IndexOf(targetTrack);
            if (idx >= 0)
            {
                _audio.CurrentQueueIndex = idx;
            }

            TimeSpan resumePos = TimeSpan.FromSeconds(Math.Max(0, lastSecs));

            if (_audio.IsAutoplayOnStartup)
            {
                _audio.PlayTrack(targetTrack);
                if (resumePos > TimeSpan.Zero)
                {
                    _audio.Seek(resumePos);
                }
            }
            else
            {
                _audio.LoadTrackPaused(targetTrack, resumePos);
            }
        }

        string onboardingDone = await _libraryDao.GetSettingAsync("onboarding_completed", "false");
        var configuredFolders = await _libraryDao.GetLibraryFoldersAsync();
        if (onboardingDone != "true" || (configuredFolders.Count == 0 && existing.Count == 0))
        {
            string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(myMusic) && !OnboardingFolders.Contains(myMusic))
            {
                OnboardingFolders.Add(myMusic);
            }
            IsOnboardingVisible = true;
        }

        // 10-second non-blocking delay before starting background enrichment on app startup
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10000);
                await Services.Online.ArtistEnrichmentService.Instance.EnqueueAndStartAsync();
            }
            catch { }
        });
    }

    // Global Tile & Navigation Commands
    [RelayCommand]
    public void PlayAlbum(AlbumModel? album)
    {
        if (album == null) return;
        var tracks = album.Tracks;
        if (tracks.Count == 0)
        {
            tracks = _cache.CachedTracks.Where(t => string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (tracks.Count == 0) return;
        _audio.PlayQueue(tracks, 0);
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null) return;
        var tracks = artist.Tracks;
        if (tracks.Count == 0)
        {
            tracks = _cache.CachedTracks.Where(t => string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (tracks.Count == 0) return;
        _audio.PlayQueue(tracks, 0);
    }

    [RelayCommand]
    public void NavigateToAlbum(string? albumName)
    {
        if (string.IsNullOrWhiteSpace(albumName)) return;
        PushCurrentNavigationState();

        var album = _cache.CachedAlbums.FirstOrDefault(a => a.Name.Equals(albumName, StringComparison.OrdinalIgnoreCase))
                 ?? _cache.CachedAlbums.FirstOrDefault(a => a.Name.Contains(albumName, StringComparison.OrdinalIgnoreCase));

        _isRestoringNavigation = true;
        try
        {
            CurrentView = "MyMusic";
            IsNowPlayingActive = false;
            MyMusic.SelectedTabIndex = 2; // Álbumes
            if (album != null)
            {
                if (album.Tracks.Count == 0)
                {
                    album.Tracks = _cache.CachedTracks
                        .Where(t => string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(t => t.TrackNumber == 0 ? uint.MaxValue : t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                MyMusic.SelectedAlbumDetail = album;
            }
            UpdateCanGoBack();
        }
        finally
        {
            _isRestoringNavigation = false;
        }
    }

    [RelayCommand]
    public void NavigateToArtist(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;
        PushCurrentNavigationState();

        var artist = _cache.CachedArtists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase))
                  ?? _cache.CachedArtists.FirstOrDefault(a => a.Name.Contains(artistName, StringComparison.OrdinalIgnoreCase))
                  ?? new ArtistModel { Name = artistName.Trim() };

        _isRestoringNavigation = true;
        try
        {
            CurrentView = "MyMusic";
            IsNowPlayingActive = false;
            MyMusic.SelectedAlbumDetail = null;
            MyMusic.SelectedTabIndex = 1; // Artistas
            UpdateCanGoBack();
        }
        finally
        {
            _isRestoringNavigation = false;
        }

        if (artist != null)
        {
            _isRestoringNavigation = true;
            try
            {
                MyMusic.OpenArtistDetail(artist);
            }
            finally
            {
                _isRestoringNavigation = false;
            }
        }
    }

    // Global Context Menu Track Commands
    [RelayCommand]
    public void PlayTrackNow(Track? track)
    {
        if (track == null) return;
        _audio.PlayTrack(track);
    }

    [RelayCommand]
    public void PlayNext(Track? track)
    {
        if (track == null) return;
        _audio.PlayNext(track);
        OnNotificationRaised(this, ("Cola de reproducción", $"Se reproducirá a continuación: {track.Title}", false));
    }

    [RelayCommand]
    public void AddTrackToQueue(Track? track)
    {
        if (track == null) return;
        _audio.AddToQueue(track, playImmediately: false);
        OnNotificationRaised(this, ("Cola de reproducción", $"Añadida a la cola: {track.Title}", false));
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(Track? track)
    {
        if (track == null) return;
        track.IsFavorite = !track.IsFavorite;
        await _libraryDao.SetTrackFavoriteAsync(track.Id, track.IsFavorite);
        await Home.LoadHomeDataAsync();
        OnNotificationRaised(this, (
            track.IsFavorite ? "Añadida a Favoritos" : "Eliminada de Favoritos",
            $"\"{track.Title}\" {(track.IsFavorite ? "marcada con ♥" : "removida de favoritos")}.",
            false
        ));
    }

    [RelayCommand]
    public async Task DeleteTrackAsync(Track? track)
    {
        if (track == null) return;
        var result = MessageBox.Show(
            $"¿Estás seguro de que deseas eliminar \"{track.Title}\" de tu biblioteca musical?",
            "Eliminar pista",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );
        if (result == MessageBoxResult.Yes)
        {
            await _libraryDao.DeleteTrackAsync(track.Id);
            _audio.Queue.Remove(track);
            await _cache.ReloadFromDatabaseAsync();
            await MyMusic.RefreshDataAsync();
            await RecentPlays.LoadRecentAsync();
            await Home.LoadHomeDataAsync();
        }
    }

    // Track Context and Global Playlist Operations
    public Track? SelectedTrackForContext { get; set; }

    // Duplicate Track Dialog
    [ObservableProperty]
    private bool _isDuplicateDialogVisible;

    [ObservableProperty]
    private string _duplicateDialogMessage = string.Empty;

    public Track? PendingDuplicateTrack { get; set; }
    public Playlist? PendingDuplicatePlaylist { get; set; }

    [RelayCommand]
    public async Task AddTrackToPlaylistWithCheckAsync(Playlist? playlist)
    {
        var track = SelectedTrackForContext ?? AudioPlayerService.Instance.CurrentTrack;
        if (track == null || playlist == null) return;
        await CheckAndAddTrackToPlaylistAsync(track, playlist);
    }

    public async Task CheckAndAddTrackToPlaylistAsync(Track track, Playlist playlist)
    {
        int count = await PlaylistDao.Instance.GetTrackCountInPlaylistAsync(playlist.Id, track.Id);
        if (count > 0)
        {
            PendingDuplicateTrack = track;
            PendingDuplicatePlaylist = playlist;
            DuplicateDialogMessage = $"Esta pista ya está en \"{playlist.Name}\" ({count} {(count == 1 ? "vez" : "veces")}). ¿Deseas añadirla de todos modos?";
            IsDuplicateDialogVisible = true;
        }
        else
        {
            await PlaylistDao.Instance.AddTrackToPlaylistAsync(playlist.Id, track.Id);
            playlist.TrackCount++;
            if (SelectedPlaylistForDetail?.Id == playlist.Id)
            {
                track.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(track);
                SelectedPlaylistForDetail.Tracks.Add(track);
            }
            ShowToast("Lista de reproducción", $"\"{track.Title}\" añadida a {playlist.Name}.", false, 3000);
        }
    }

    [RelayCommand]
    public async Task AddDuplicateConfirmedAsync()
    {
        IsDuplicateDialogVisible = false;
        if (PendingDuplicateTrack != null && PendingDuplicatePlaylist != null)
        {
            await PlaylistDao.Instance.AddTrackToPlaylistAsync(PendingDuplicatePlaylist.Id, PendingDuplicateTrack.Id);
            PendingDuplicatePlaylist.TrackCount++;
            if (SelectedPlaylistForDetail?.Id == PendingDuplicatePlaylist.Id)
            {
                PendingDuplicateTrack.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(PendingDuplicateTrack);
                SelectedPlaylistForDetail.Tracks.Add(PendingDuplicateTrack);
            }
            ShowToast("Lista de reproducción", $"\"{PendingDuplicateTrack.Title}\" añadida (duplicada) a {PendingDuplicatePlaylist.Name}.", false, 3000);
            PendingDuplicateTrack = null;
            PendingDuplicatePlaylist = null;
        }
    }

    [RelayCommand]
    public void CancelDuplicate()
    {
        IsDuplicateDialogVisible = false;
        PendingDuplicateTrack = null;
        PendingDuplicatePlaylist = null;
    }

    // New Playlist Modal Dialog
    [ObservableProperty]
    private bool _isNewPlaylistDialogVisible;

    [ObservableProperty]
    private string _newPlaylistDialogName = string.Empty;

    private Track? _pendingTrackForNewPlaylist;

    [RelayCommand]
    public void OpenCreatePlaylistDialog(Track? track)
    {
        _pendingTrackForNewPlaylist = track ?? SelectedTrackForContext ?? AudioPlayerService.Instance.CurrentTrack;
        NewPlaylistDialogName = $"Mi Lista {Playlists.Playlists.Count + 1}";
        IsNewPlaylistDialogVisible = true;
    }

    [RelayCommand]
    public void CancelNewPlaylistDialog()
    {
        IsNewPlaylistDialogVisible = false;
        _pendingTrackForNewPlaylist = null;
    }

    [RelayCommand]
    public async Task ConfirmNewPlaylistDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistDialogName)) return;
        string name = NewPlaylistDialogName.Trim();
        IsNewPlaylistDialogVisible = false;

        var tracksToAdd = _pendingTrackForNewPlaylist != null ? new[] { _pendingTrackForNewPlaylist } : null;
        var created = await PlaylistDao.Instance.CreatePlaylistAsync(name, tracksToAdd);
        Playlists.Playlists.Add(created);

        if (_pendingTrackForNewPlaylist != null)
        {
            ShowToast("Lista de reproducción", $"Lista \"{name}\" creada con \"{_pendingTrackForNewPlaylist.Title}\".", false, 3000);
        }
        else
        {
            ShowToast("Lista de reproducción", $"Lista \"{name}\" creada.", false, 3000);
        }
        _pendingTrackForNewPlaylist = null;
        await OpenPlaylistDetailAsync(created);
    }

    // Edit Playlist Modal Dialog
    [ObservableProperty]
    private bool _isEditPlaylistDialogVisible;

    [ObservableProperty]
    private string _editPlaylistName = string.Empty;

    [ObservableProperty]
    private string _editPlaylistDescription = string.Empty;

    [ObservableProperty]
    private string? _editPlaylistCoverPath;

    [ObservableProperty]
    private ImageSource? _editPlaylistCoverPreview;

    [RelayCommand]
    public void OpenEditPlaylistDialog(Playlist? playlist = null)
    {
        playlist ??= SelectedPlaylistForDetail;
        if (playlist == null) return;
        SelectedPlaylistForDetail = playlist;
        EditPlaylistName = playlist.Name;
        EditPlaylistDescription = playlist.Description;
        EditPlaylistCoverPath = playlist.CustomCoverPath;
        EditPlaylistCoverPreview = playlist.CoverImage;
        IsEditPlaylistDialogVisible = true;
    }

    [RelayCommand]
    public async Task PlayPlaylistDirectAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        await OpenPlaylistDetailAsync(playlist);
        PlayAllPlaylist();
    }

    [RelayCommand]
    public void CancelEditPlaylistDialog()
    {
        IsEditPlaylistDialogVisible = false;
    }

    [RelayCommand]
    public void BrowsePlaylistCover()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleccionar portada para la lista",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            EditPlaylistCoverPath = dlg.FileName;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                EditPlaylistCoverPreview = bmp;
            }
            catch { }
        }
    }

    [RelayCommand]
    public void GeneratePlaylistCollage()
    {
        if (SelectedPlaylistForDetail == null || SelectedPlaylistForDetail.Tracks.Count == 0)
        {
            ShowToast("Collage", "La lista necesita al menos una canción para generar portada.", true, 3000);
            return;
        }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "BenyComes", "Playlists");
            Directory.CreateDirectory(dir);
            var collagePath = Path.Combine(dir, $"playlist_{SelectedPlaylistForDetail.Id}_cover.png");

            var tracksWithArt = SelectedPlaylistForDetail.Tracks
                .Select(t => t.AlbumArtImage)
                .Where(img => img != null)
                .Take(4)
                .ToList();

            if (tracksWithArt.Count == 0)
            {
                ShowToast("Collage", "No se encontraron carátulas en las canciones de esta lista.", true, 3000);
                return;
            }

            int size = 400;
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 25, 32)), null, new Rect(0, 0, size, size));

                if (tracksWithArt.Count == 1)
                {
                    dc.DrawImage(tracksWithArt[0], new Rect(0, 0, size, size));
                }
                else
                {
                    int half = size / 2;
                    for (int i = 0; i < 4; i++)
                    {
                        int imgIdx = i % tracksWithArt.Count;
                        int x = (i % 2) * half;
                        int y = (i / 2) * half;
                        dc.DrawImage(tracksWithArt[imgIdx], new Rect(x, y, half, half));
                    }
                }
            }
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var stream = File.Create(collagePath))
            {
                encoder.Save(stream);
            }

            EditPlaylistCoverPath = collagePath;
            EditPlaylistCoverPreview = rtb;
            ShowToast("Collage", "Collage de carátulas generado con éxito.", false, 2500);
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"No se pudo generar el collage: {ex.Message}", true, 3000);
        }
    }

    [RelayCommand]
    public async Task SaveEditPlaylistAsync()
    {
        if (SelectedPlaylistForDetail == null || string.IsNullOrWhiteSpace(EditPlaylistName)) return;

        SelectedPlaylistForDetail.Name = EditPlaylistName.Trim();
        SelectedPlaylistForDetail.Description = EditPlaylistDescription?.Trim() ?? "";
        SelectedPlaylistForDetail.CustomCoverPath = EditPlaylistCoverPath;
        SelectedPlaylistForDetail.RefreshCoverImage();

        await PlaylistDao.Instance.UpdatePlaylistMetadataAsync(
            SelectedPlaylistForDetail.Id,
            SelectedPlaylistForDetail.Name,
            SelectedPlaylistForDetail.Description,
            SelectedPlaylistForDetail.CustomCoverPath
        );

        IsEditPlaylistDialogVisible = false;
        ShowToast("Lista de reproducción", "Lista actualizada correctamente.", false, 2500);
    }

    // Playlist Suggestions & Track Reordering
    public ObservableCollection<Track> SuggestedTracks { get; } = new();

    [ObservableProperty]
    private bool _isSuggestedExpanded = false;

    [RelayCommand]
    public void ToggleSuggestedSection()
    {
        IsSuggestedExpanded = !IsSuggestedExpanded;
    }

    public void LoadSuggestedTracks(Playlist playlist)
    {
        SuggestedTracks.Clear();
        var allTracks = LibraryCacheService.Instance.CachedTracks;
        if (allTracks.Count == 0) return;

        var playlistTrackIds = playlist.Tracks.Select(t => t.Id).ToHashSet();

        var playlistGenres = playlist.Tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
            .Select(t => t.Genre)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var playlistArtists = playlist.Tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .Select(t => t.Artist)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = allTracks
            .Where(t => !playlistTrackIds.Contains(t.Id))
            .OrderByDescending(t => (playlistArtists.Contains(t.Artist) ? 2 : 0) + (playlistGenres.Contains(t.Genre) ? 1 : 0))
            .ThenBy(t => t.Title)
            .Take(10)
            .ToList();

        foreach (var c in candidates)
        {
            c.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(c);
            SuggestedTracks.Add(c);
        }
    }

    [RelayCommand]
    public async Task AddSuggestedTrackAsync(Track? track)
    {
        if (track == null || SelectedPlaylistForDetail == null) return;
        await PlaylistDao.Instance.AddTrackToPlaylistAsync(SelectedPlaylistForDetail.Id, track.Id);
        track.AlbumArtImage = MetadataService.Instance.GetAlbumArtImage(track);
        SelectedPlaylistForDetail.Tracks.Add(track);
        SelectedPlaylistForDetail.TrackCount = SelectedPlaylistForDetail.Tracks.Count;
        SuggestedTracks.Remove(track);
        ShowToast("Lista de reproducción", $"\"{track.Title}\" añadida a {SelectedPlaylistForDetail.Name}.", false, 2500);
    }

    [RelayCommand]
    public async Task MoveTrackUpAsync(Track? track)
    {
        if (track == null || SelectedPlaylistForDetail == null) return;
        int idx = SelectedPlaylistForDetail.Tracks.IndexOf(track);
        if (idx > 0)
        {
            SelectedPlaylistForDetail.Tracks.Move(idx, idx - 1);
            await PlaylistDao.Instance.UpdateTrackPositionsAsync(SelectedPlaylistForDetail.Id, SelectedPlaylistForDetail.Tracks);
        }
    }

    [RelayCommand]
    public async Task MoveTrackDownAsync(Track? track)
    {
        if (track == null || SelectedPlaylistForDetail == null) return;
        int idx = SelectedPlaylistForDetail.Tracks.IndexOf(track);
        if (idx >= 0 && idx < SelectedPlaylistForDetail.Tracks.Count - 1)
        {
            SelectedPlaylistForDetail.Tracks.Move(idx, idx + 1);
            await PlaylistDao.Instance.UpdateTrackPositionsAsync(SelectedPlaylistForDetail.Id, SelectedPlaylistForDetail.Tracks);
        }
    }

    [RelayCommand]
    public async Task RemoveTrackFromPlaylistAsync(Track? track)
    {
        if (track == null || SelectedPlaylistForDetail == null) return;
        await PlaylistDao.Instance.RemoveTrackFromPlaylistAsync(SelectedPlaylistForDetail.Id, track.Id);
        SelectedPlaylistForDetail.Tracks.Remove(track);
        SelectedPlaylistForDetail.TrackCount = SelectedPlaylistForDetail.Tracks.Count;
        await PlaylistDao.Instance.UpdateTrackPositionsAsync(SelectedPlaylistForDetail.Id, SelectedPlaylistForDetail.Tracks);
        LoadSuggestedTracks(SelectedPlaylistForDetail);
        ShowToast("Lista de reproducción", $"\"{track.Title}\" eliminada de {SelectedPlaylistForDetail.Name}.", false, 2500);
    }

    [RelayCommand]
    public async Task DeletePlaylistWithNavigationAsync(Playlist? playlist)
    {
        playlist ??= SelectedPlaylistForDetail;
        if (playlist == null) return;

        var result = MessageBox.Show(
            $"¿Estás seguro de que deseas eliminar la lista \"{playlist.Name}\"?",
            "Eliminar Lista de Reproducción",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );
        if (result != MessageBoxResult.Yes) return;

        int oldIndex = Playlists.Playlists.IndexOf(playlist);
        await PlaylistDao.Instance.DeletePlaylistAsync(playlist.Id);
        Playlists.Playlists.Remove(playlist);

        if (Playlists.Playlists.Count > 0)
        {
            int newIndex = Math.Clamp(oldIndex, 0, Playlists.Playlists.Count - 1);
            var adjacentPlaylist = Playlists.Playlists[newIndex];
            await OpenPlaylistDetailAsync(adjacentPlaylist);
        }
        else
        {
            SelectedPlaylistForDetail = null;
            NavigateTo("Home");
            ShowToast("Listas de reproducción", "No hay listas de reproducción restantes", false, 3000);
        }
    }

    [RelayCommand]
    public void PlayPlaylistTrack(Track? track)
    {
        if (track == null || SelectedPlaylistForDetail == null) return;
        var list = SelectedPlaylistForDetail.Tracks.ToList();
        int idx = list.IndexOf(track);
        if (idx < 0) return;
        _audio.PlayQueue(list, idx);
    }

    [RelayCommand]
    public void PlayAllPlaylist()
    {
        if (SelectedPlaylistForDetail == null || SelectedPlaylistForDetail.Tracks.Count == 0) return;
        _audio.PlayQueue(SelectedPlaylistForDetail.Tracks.ToList(), 0);
    }

    [RelayCommand]
    public void ExportPlaylist()
    {
        var playlist = SelectedPlaylistForDetail;
        if (playlist == null) return;
        if (playlist.Tracks.Count == 0)
        {
            ShowToast("Exportar lista", "La lista está vacía.", true, 3000);
            return;
        }

        var invalid = Path.GetInvalidFileNameChars();
        string safeName = new string(playlist.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar lista de reproducción",
            FileName = safeName,
            DefaultExt = ".m3u8",
            AddExtension = true,
            Filter = "Archivos M3U8 (*.m3u8)|*.m3u8|Archivos M3U (*.m3u)|*.m3u"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var t in playlist.Tracks)
            {
                int secs = (int)Math.Round(t.Duration.TotalSeconds);
                sb.AppendLine($"#EXTINF:{secs},{t.Artist} - {t.Title}");
                sb.AppendLine(Path.GetFullPath(t.FilePath));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(false));
            ShowToast("Exportar lista", "Lista exportada correctamente", false, 3000);
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"No se pudo exportar: {ex.Message}", true, 4000);
        }
    }

    public async void ShowToast(string title, string message, bool isWarning = false, int durationMs = 3000)
    {
        NotificationTitle = title;
        NotificationMessage = message;
        IsNotificationWarning = isWarning;
        IsNotificationVisible = true;
        if (durationMs > 0)
        {
            await Task.Delay(durationMs);
            if (NotificationTitle == title && NotificationMessage == message)
            {
                IsNotificationVisible = false;
            }
        }
    }

    // Onboarding Commands
    [RelayCommand]
    public void AddOnboardingFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar Carpeta de Música"
        };
        if (dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName))
        {
            if (!OnboardingFolders.Contains(dialog.FolderName))
            {
                OnboardingFolders.Add(dialog.FolderName);
            }
        }
    }

    [RelayCommand]
    public void AddDefaultMusicFolder()
    {
        string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(myMusic) && !OnboardingFolders.Contains(myMusic))
        {
            OnboardingFolders.Add(myMusic);
        }
    }

    [RelayCommand]
    public void RemoveOnboardingFolder(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OnboardingFolders.Remove(folder);
        }
    }

    [RelayCommand]
    public async Task CompleteOnboardingAsync()
    {
        if (OnboardingFolders.Count == 0)
        {
            string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(myMusic) && !OnboardingFolders.Contains(myMusic))
            {
                OnboardingFolders.Add(myMusic);
            }
        }

        foreach (var f in OnboardingFolders)
        {
            await _libraryDao.AddLibraryFolderAsync(f);
            if (!Settings.LibraryFolders.Contains(f))
            {
                Settings.LibraryFolders.Add(f);
            }
        }

        await _libraryDao.SetSettingAsync("onboarding_completed", "true");
        IsOnboardingVisible = false;

        if (OnboardingFolders.Count > 0)
        {
            await ScanDirectoriesOrFilesAsync(OnboardingFolders.ToList());
            ShowToast("¡Bienvenido a BenyComes!", "Tu biblioteca ha sido indexada correctamente.", false, 4000);
        }
        else
        {
            ShowToast("¡Bienvenido a BenyComes!", "Puedes agregar carpetas de música en cualquier momento desde Ajustes.", false, 4000);
        }
    }

    [RelayCommand]
    public async Task SkipOnboardingAsync()
    {
        await _libraryDao.SetSettingAsync("onboarding_completed", "true");
        IsOnboardingVisible = false;
        ShowToast("BenyComes", "Puedes agregar carpetas en cualquier momento desde Ajustes.", false, 3000);
    }

    [RelayCommand]
    public void OpenOnboarding()
    {
        IsOnboardingVisible = true;
    }

    // Modal Track Properties State
    [ObservableProperty]
    private bool _isTrackPropertiesModalOpen;

    [ObservableProperty]
    private Track? _selectedTrackForProperties;

    [RelayCommand]
    public void ShowTrackProperties(Track? track)
    {
        track ??= _audio.CurrentTrack ?? NowPlaying.CurrentTrack;
        if (track == null) return;
        SelectedTrackForProperties = track;
        IsTrackPropertiesModalOpen = true;
    }

    [RelayCommand]
    public void ShowCurrentTrackProperties()
    {
        var track = _audio.CurrentTrack ?? NowPlaying.CurrentTrack;
        if (track == null) return;
        SelectedTrackForProperties = track;
        IsTrackPropertiesModalOpen = true;
    }

    [RelayCommand]
    public void CloseTrackProperties()
    {
        IsTrackPropertiesModalOpen = false;
        SelectedTrackForProperties = null;
    }

    [RelayCommand]
    public void OpenTrackInExplorer(Track? track)
    {
        track ??= SelectedTrackForProperties;
        if (track == null || !File.Exists(track.FilePath)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{track.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }

    // ==============================================================
    // ARTIST PHOTO GALLERY & BIO MODALS
    // ==============================================================
    [ObservableProperty]
    private bool _isArtistGalleryModalOpen;

    [ObservableProperty]
    private string _galleryArtistName = string.Empty;

    public ObservableCollection<string> GalleryAvailableArtists { get; } = new();

    [ObservableProperty]
    private string _selectedGalleryArtist = string.Empty;

    [ObservableProperty]
    private bool _hasMultipleGalleryArtists;

    public ObservableCollection<string> GallerySources { get; } = new()
    {
        "Automático",
        "Fanart.tv",
        "Deezer"
    };

    [ObservableProperty]
    private string _selectedGallerySource = "Automático";

    partial void OnSelectedGalleryArtistChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase))
            return;

        GalleryArtistName = value;
        _ = LoadGalleryPhotosAsync(value);

        if (NowPlaying.CollaborationArtists.Contains(value) && NowPlaying.SelectedCollaborationArtist != value)
        {
            NowPlaying.SelectedCollaborationArtist = value;
        }
    }

    [ObservableProperty]
    private bool _isGalleryLoading;

    public ObservableCollection<ArtistPhotoItem> GalleryPhotos { get; } = new();

    [ObservableProperty]
    private bool _isArtistBioModalOpen;

    [ObservableProperty]
    private string _bioModalArtistName = string.Empty;

    [ObservableProperty]
    private string _bioModalSummary = string.Empty;

    [ObservableProperty]
    private string _bioModalContent = string.Empty;

    [ObservableProperty]
    private BitmapImage? _bioModalAvatar;

    [RelayCommand]
    public async Task OpenArtistGalleryAsync(string? artistName)
    {
        string? rawTrackArtist = AudioPlayerService.Instance.CurrentTrack?.Artist ?? NowPlaying.Artist;

        // If artistName is not explicitly given, try selected collaboration artist or current track artist
        if (string.IsNullOrWhiteSpace(artistName))
        {
            if (!string.IsNullOrWhiteSpace(NowPlaying.SelectedCollaborationArtist) && !NowPlaying.SelectedCollaborationArtist.Equals("Todos", StringComparison.OrdinalIgnoreCase))
            {
                artistName = NowPlaying.SelectedCollaborationArtist;
            }
            else
            {
                artistName = rawTrackArtist;
            }
        }

        if (string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Galería de artista", "No hay ningún artista seleccionado.", true, 3000);
            return;
        }

        // Split artists to detect collaborations
        string stringToSplit = !string.IsNullOrWhiteSpace(rawTrackArtist) && rawTrackArtist.Contains(artistName, StringComparison.OrdinalIgnoreCase)
            ? rawTrackArtist
            : artistName;

        var split = ArtistSplitterService.Instance.SplitArtists(stringToSplit);

        GalleryAvailableArtists.Clear();
        if (split.Count > 1)
        {
            foreach (var a in split)
            {
                if (!GalleryAvailableArtists.Contains(a))
                    GalleryAvailableArtists.Add(a);
            }
            HasMultipleGalleryArtists = true;

            var match = GalleryAvailableArtists.FirstOrDefault(a => a.Equals(artistName, StringComparison.OrdinalIgnoreCase));
            SelectedGalleryArtist = match ?? GalleryAvailableArtists[0];
        }
        else
        {
            GalleryAvailableArtists.Add(artistName);
            HasMultipleGalleryArtists = false;
            SelectedGalleryArtist = artistName;
        }

        GalleryArtistName = SelectedGalleryArtist;
        IsArtistGalleryModalOpen = true;
        await LoadGalleryPhotosAsync(GalleryArtistName);
    }

    public void OpenArtistGallery(string? artistName = null) => _ = OpenArtistGalleryAsync(artistName);

    [RelayCommand]
    public void CloseArtistGallery()
    {
        IsArtistGalleryModalOpen = false;
    }

    public async Task LoadGalleryPhotosAsync(string artistName)
    {
        IsGalleryLoading = true;
        GalleryPhotos.Clear();

        try
        {
            var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(artistName);
            string artistDir = Services.Online.LastFmService.Instance.GetArtistDirectory(artistName);
            string primaryTxt = Path.Combine(artistDir, "primary.txt");
            string primaryPath = File.Exists(primaryTxt) ? File.ReadAllText(primaryTxt).Trim() : "";
            var omittedSet = Services.Online.LastFmService.Instance.GetOmittedImages(artistDir);

            var allImages = Services.Online.LastFmService.Instance.GetLocalArtistImages(artistDir, includeOmitted: true);
            var activeImages = Services.Online.LastFmService.Instance.GetLocalArtistImages(artistDir, includeOmitted: false);
            if (activeImages.Count > 0 && (string.IsNullOrEmpty(primaryPath) || omittedSet.Contains(Path.GetFileName(primaryPath))))
            {
                primaryPath = activeImages[0];
            }

            foreach (var imgPath in allImages)
            {
                var bi = LoadImagePreview(imgPath);
                bool isOmitted = omittedSet.Contains(Path.GetFileName(imgPath));
                GalleryPhotos.Add(new ArtistPhotoItem
                {
                    FilePath = imgPath,
                    IsPrimary = !isOmitted && string.Equals(imgPath, primaryPath, StringComparison.OrdinalIgnoreCase),
                    IsOmitted = isOmitted,
                    IsIncludedInRotation = !isOmitted,
                    ImagePreview = bi
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery] Error loading photos: {ex.Message}");
        }
        finally
        {
            IsGalleryLoading = false;
        }
    }

    [RelayCommand]
    public async Task SetPrimaryArtistPhotoAsync(ArtistPhotoItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(GalleryArtistName)) return;

        await Services.Online.LastFmService.Instance.SetPrimaryArtistImageAsync(GalleryArtistName, item.FilePath);

        item.IsOmitted = false;
        foreach (var p in GalleryPhotos)
        {
            p.IsPrimary = p == item;
        }

        // Invalidate in cache & models
        var artistModel = MyMusic.Artists.FirstOrDefault(a => a.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase));
        artistModel?.RefreshAvatar(item.FilePath);

        if (MyMusic.SelectedArtistDetail != null && MyMusic.SelectedArtistDetail.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase))
        {
            MyMusic.SelectedArtistDetail.RefreshAvatar(item.FilePath);
        }

        _ = NowPlaying.RefreshArtistBackgroundAsync(GalleryArtistName);
        ShowToast("Foto principal", $"Foto establecida como principal para {GalleryArtistName}.", false, 2500);
    }

    [RelayCommand]
    public async Task ToggleOmitArtistPhotoAsync(ArtistPhotoItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(GalleryArtistName)) return;

        bool isNowOmitted = await Services.Online.LastFmService.Instance.ToggleOmitArtistImageAsync(GalleryArtistName, item.FilePath);
        item.IsOmitted = isNowOmitted;

        if (isNowOmitted && item.IsPrimary)
        {
            item.IsPrimary = false;
            var nextActive = GalleryPhotos.FirstOrDefault(p => !p.IsOmitted);
            if (nextActive != null)
            {
                await Services.Online.LastFmService.Instance.SetPrimaryArtistImageAsync(GalleryArtistName, nextActive.FilePath);
                nextActive.IsPrimary = true;
            }
        }

        var artistModel = MyMusic.Artists.FirstOrDefault(a => a.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase));
        artistModel?.RefreshAvatar();

        if (MyMusic.SelectedArtistDetail != null && MyMusic.SelectedArtistDetail.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase))
        {
            MyMusic.SelectedArtistDetail.RefreshAvatar();
        }

        _ = NowPlaying.RefreshArtistBackgroundAsync(GalleryArtistName);
        ShowToast(
            isNowOmitted ? "Foto omitida" : "Foto incluida",
            isNowOmitted ? "La imagen ya no se mostrará en el fondo ni como avatar." : "La imagen se ha vuelto a incluir en la galería.",
            false,
            2500
        );
    }

    [RelayCommand]
    public async Task AddLocalArtistPhotoAsync()
    {
        if (string.IsNullOrWhiteSpace(GalleryArtistName)) return;

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Seleccionar foto para {GalleryArtistName}",
            Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Todos los archivos (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true && File.Exists(ofd.FileName))
        {
            string? copied = await Services.Online.LastFmService.Instance.AddCustomArtistImageAsync(GalleryArtistName, ofd.FileName);
            if (!string.IsNullOrEmpty(copied))
            {
                await LoadGalleryPhotosAsync(GalleryArtistName);
                var artistModel = MyMusic.Artists.FirstOrDefault(a => a.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase));
                artistModel?.RefreshAvatar(copied);
                if (MyMusic.SelectedArtistDetail != null && MyMusic.SelectedArtistDetail.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase))
                {
                    MyMusic.SelectedArtistDetail.RefreshAvatar(copied);
                }
                _ = NowPlaying.RefreshArtistBackgroundAsync(GalleryArtistName);
                ShowToast("Foto añadida", "Imagen agregada a la galería del artista con éxito.", false, 2500);
            }
        }
    }

    [RelayCommand]
    public async Task RefreshGalleryFromLastFmAsync()
    {
        if (string.IsNullOrWhiteSpace(GalleryArtistName)) return;
        IsGalleryLoading = true;
        ShowToast("Descargando", $"Consultando nuevas imágenes ({SelectedGallerySource}) y biografía para {GalleryArtistName}...", false, 3000);

        try
        {
            var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(
                GalleryArtistName, 
                forceRefresh: true, 
                source: SelectedGallerySource
            );
            await LoadGalleryPhotosAsync(GalleryArtistName);

            var artistModel = MyMusic.Artists.FirstOrDefault(a => a.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase));
            if (artistModel != null)
            {
                artistModel.BiographySummary = info.BioSummary;
                artistModel.BiographyFull = info.BioFull;
                artistModel.HasBiography = !string.IsNullOrWhiteSpace(info.BioSummary);
                artistModel.RefreshAvatar();
            }

            if (MyMusic.SelectedArtistDetail != null && MyMusic.SelectedArtistDetail.Name.Equals(GalleryArtistName, StringComparison.OrdinalIgnoreCase))
            {
                MyMusic.SelectedArtistDetail.BiographySummary = info.BioSummary;
                MyMusic.SelectedArtistDetail.BiographyFull = info.BioFull;
                MyMusic.SelectedArtistDetail.HasBiography = !string.IsNullOrWhiteSpace(info.BioSummary);
                MyMusic.SelectedArtistDetail.RefreshAvatar();
            }

            _ = NowPlaying.RefreshArtistBackgroundAsync(GalleryArtistName);
            ShowToast("Galería actualizada", $"Se obtuvieron {GalleryPhotos.Count} fotos para {GalleryArtistName}.", false, 3000);
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"No se pudieron descargar imágenes: {ex.Message}", true, 3000);
        }
        finally
        {
            IsGalleryLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenArtistBioAsync(ArtistModel? artist)
    {
        artist ??= MyMusic.SelectedArtistDetail;
        if (artist == null) return;

        BioModalArtistName = artist.Name;
        BioModalAvatar = artist.AvatarImageSource;
        IsArtistBioModalOpen = true;

        if (string.IsNullOrWhiteSpace(artist.BiographyFull))
        {
            var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(artist.Name);
            artist.BiographySummary = info.BioSummary;
            artist.BiographyFull = string.IsNullOrWhiteSpace(info.BioFull) ? info.BioSummary : info.BioFull;
            artist.HasBiography = !string.IsNullOrWhiteSpace(artist.BiographySummary);
        }

        BioModalSummary = artist.BiographySummary;
        BioModalContent = string.IsNullOrWhiteSpace(artist.BiographyFull)
            ? (string.IsNullOrWhiteSpace(artist.BiographySummary) ? "No hay biografía disponible para este artista. Puedes configurarla activando Last.fm en Ajustes." : artist.BiographySummary)
            : artist.BiographyFull;
    }

    [RelayCommand]
    public void CloseArtistBio()
    {
        IsArtistBioModalOpen = false;
    }

    [RelayCommand]
    public async Task ChangeArtistPhotoFromExplorerAsync(ArtistModel? artist)
    {
        artist ??= MyMusic.SelectedArtistDetail;
        if (artist == null) return;

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Seleccionar foto para {artist.Name}",
            Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Todos los archivos (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true && File.Exists(ofd.FileName))
        {
            string? copied = await Services.Online.LastFmService.Instance.AddCustomArtistImageAsync(artist.Name, ofd.FileName);
            if (!string.IsNullOrEmpty(copied))
            {
                artist.RefreshAvatar(copied);
                ShowToast("Foto de artista", $"Foto de {artist.Name} actualizada correctamente.", false, 2500);
            }
        }
    }

    [RelayCommand]
    public async Task RedownloadArtistFromLastFmAsync(ArtistModel? artist)
    {
        artist ??= MyMusic.SelectedArtistDetail;
        if (artist == null) return;

        ShowToast("Last.fm", $"Buscando información e imágenes para {artist.Name}...", false, 3000);
        var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(artist.Name, forceRefresh: true);

        artist.BiographySummary = info.BioSummary;
        artist.BiographyFull = info.BioFull;
        artist.HasBiography = !string.IsNullOrWhiteSpace(info.BioSummary);
        artist.RefreshAvatar();

        ShowToast("Last.fm", $"Información de {artist.Name} actualizada con éxito.", false, 3000);
    }

    private static BitmapImage? LoadImagePreview(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 200;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // 3-Dots Action Menu & Equalizer Commands
    [RelayCommand]
    public void OpenCurrentQueue()
    {
        IsNowPlayingActive = true;
        IsNowPlayingQueueExpanded = true;
    }

    [RelayCommand]
    public void ViewCurrentAlbum()
    {
        var albumName = _audio.CurrentTrack?.Album ?? NowPlaying.Album;
        if (!string.IsNullOrWhiteSpace(albumName))
        {
            NavigateToAlbum(albumName);
        }
    }

    [RelayCommand]
    public void ViewCurrentArtist()
    {
        var artistName = _audio.CurrentTrack?.Artist ?? NowPlaying.Artist;
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            NavigateToArtist(artistName);
        }
    }

    [RelayCommand]
    public void AddRelatedTracks()
    {
        var current = _audio.CurrentTrack;
        if (current == null && _cache.CachedTracks.Count > 0)
        {
            current = _cache.CachedTracks[0];
        }
        if (current == null) return;

        var existingPaths = _audio.Queue.Select(t => t.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var related = _cache.CachedTracks
            .Where(t => !existingPaths.Contains(t.FilePath) &&
                        ((!string.IsNullOrWhiteSpace(current.Artist) && t.Artist.Contains(current.Artist, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrWhiteSpace(current.Genre) && string.Equals(t.Genre, current.Genre, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(_ => Random.Shared.Next())
            .Take(10)
            .ToList();

        if (related.Count == 0)
        {
            related = _cache.CachedTracks
                .Where(t => !existingPaths.Contains(t.FilePath))
                .OrderBy(_ => Random.Shared.Next())
                .Take(8)
                .ToList();
        }

        if (related.Count > 0)
        {
            _audio.AddRangeToQueue(related, playFirst: false);
            OnNotificationRaised(this, (
                "Pistas relacionadas añadidas",
                $"Se añadieron {related.Count} pistas sugeridas por artista/género a la cola.",
                false
            ));
        }
        else
        {
            OnNotificationRaised(this, (
                "Pistas relacionadas",
                "Todas las pistas relacionadas disponibles ya están en la cola actual.",
                false
            ));
        }
    }

    [RelayCommand]
    public void StopMusic()
    {
        _audio.Stop();
        ForceUiPlaybackUpdate(_audio.CurrentTrack, false);
        OnNotificationRaised(this, ("Reproducción detenida", "El motor de audio se ha detenido.", false));
    }

    [RelayCommand]
    public void ToggleEqualizer()
    {
        IsEqualizerOpen = !IsEqualizerOpen;
        if (IsEqualizerOpen)
        {
            IsDeviceSelectorOpen = false;
            IsQueueDrawerOpen = false;
            IsPlaylistsDrawerOpen = false;
        }
    }

    [RelayCommand]
    public void CloseEqualizer()
    {
        IsEqualizerOpen = false;
    }

    // Open-Source Licenses Modal
    [ObservableProperty]
    private bool _isLicensesModalOpen;

    [RelayCommand]
    public void OpenLicensesModal()
    {
        IsLicensesModalOpen = true;
    }

    [RelayCommand]
    public void CloseLicensesModal()
    {
        IsLicensesModalOpen = false;
    }
}
