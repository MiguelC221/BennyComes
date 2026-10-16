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

    public void ForceUiPlaybackUpdate(Track? currentTrack, bool isPlaying)
    {
        var app = Application.Current;
        if (app == null) return;

        Action update = () =>
        {
            if (currentTrack != null)
            {
                NowPlaying.CurrentTrack = currentTrack;
                NowPlaying.Title = string.IsNullOrWhiteSpace(currentTrack.Title) ? "Canción desconocida" : currentTrack.Title;
                NowPlaying.Artist = string.IsNullOrWhiteSpace(currentTrack.Artist) ? "Artista desconocido" : currentTrack.Artist;
                NowPlaying.Album = currentTrack.Album ?? "";
                NowPlaying.ArtistAndAlbum = currentTrack.ArtistAndAlbum;

                var art = MetadataService.Instance.GetHighResAlbumArt(currentTrack);
                NowPlaying.AlbumArt = art;
                NowPlaying.BackgroundImage = MetadataService.Instance.GetBlurredBackgroundArt(currentTrack);
                ThemeService.Instance.OnTrackArtChanged(art);

                NowPlaying.TotalDurationSeconds = Math.Max(1, currentTrack.Duration.TotalSeconds);
                NowPlaying.TotalTimeText = currentTrack.DurationFormatted;
            }

            IsPlaying = isPlaying;
            NowPlaying.IsPlaying = isPlaying;

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

        IsNowPlayingActive = false;
        CurrentView = viewName;

        if (viewName == "Home")
        {
            _ = Home.LoadHomeDataAsync();
        }
        else if (viewName == "MyMusic")
        {
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
        if (CurrentView != "Search" && !string.IsNullOrWhiteSpace(value))
        {
            CurrentView = "Search";
            IsNowPlayingActive = false;
        }
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
        string[] extensions = { ".flac", ".ogg", ".wav", ".mp3", ".m4a", ".aac", ".opus", ".wma" };
        var filesToProcess = new List<string>();

        foreach (var path in paths)
        {
            if (File.Exists(path) && extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                filesToProcess.Add(path);
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    filesToProcess.AddRange(files);
                }
                catch { }
            }
        }

        if (filesToProcess.Count == 0) return;

        IsScanningOverlayVisible = true;
        ScanOverlayTitle = "Sincronizando tu biblioteca musical y carátulas...";
        ScanOverlayCountText = $"Preparando {filesToProcess.Count} pistas...";
        ScanOverlayCurrentFile = "";
        ScanOverlayProgress = 0.0;

        bool wasEmpty = _audio.Queue.Count == 0;
        var newTracks = new List<Track>(filesToProcess.Count);

        await Task.Run(async () =>
        {
            var progress = new Progress<ScanProgressReport>(r =>
            {
                ScanOverlayCountText = r.StatusText;
                ScanOverlayCurrentFile = r.CurrentFile;
                ScanOverlayProgress = r.ProgressPercentage;
            });

            int total = filesToProcess.Count;
            int current = 0;

            foreach (var file in filesToProcess)
            {
                current++;
                var track = await MetadataService.Instance.LoadTrackMetadataAsync(file);
                newTracks.Add(track);

                if (current % 2 == 0 || current == total)
                {
                    ((IProgress<ScanProgressReport>)progress).Report(new ScanProgressReport
                    {
                        Current = current,
                        Total = total,
                        CurrentFile = Path.GetFileName(file)
                    });
                }
            }

            // High-speed SQLite batch insert inside transaction
            await _libraryDao.InsertOrUpdateTracksBatchAsync(newTracks);
        });

        foreach (var t in newTracks)
        {
            _audio.AddToQueue(t, playImmediately: false);
        }

        // Reload memory and SQLite cache for instant switching
        await LibraryCacheService.Instance.ReloadFromDatabaseAsync();
        await RecentPlays.LoadRecentAsync();

        IsScanningOverlayVisible = false;
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
        await _cache.InitializeAsync();

        string lastFile = await _libraryDao.GetSettingAsync("last_played_file", "");
        string posStr = await _libraryDao.GetSettingAsync("last_played_position", "0");
        string queueStr = await _libraryDao.GetSettingAsync("last_queue_files", "");
        double.TryParse(posStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lastSecs);

        var existing = await _libraryDao.GetAllTracksAsync();

        if (!string.IsNullOrWhiteSpace(queueStr))
        {
            var paths = queueStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var trackMap = existing.ToDictionary(t => t.FilePath, StringComparer.OrdinalIgnoreCase);

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
        else
        {
            await GenerateDemoTracksAsync();
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

        await MyMusic.RefreshDataAsync();
        await RecentPlays.LoadRecentAsync();
        await Home.LoadHomeDataAsync();
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
        var album = _cache.CachedAlbums.FirstOrDefault(a => a.Name.Equals(albumName, StringComparison.OrdinalIgnoreCase))
                 ?? _cache.CachedAlbums.FirstOrDefault(a => a.Name.Contains(albumName, StringComparison.OrdinalIgnoreCase));

        CurrentView = "MyMusic";
        IsNowPlayingActive = false;
        MyMusic.SelectedTabIndex = 2; // Álbumes
        if (album != null)
        {
            MyMusic.OpenAlbumDetail(album);
        }
    }

    [RelayCommand]
    public void NavigateToArtist(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;
        var artist = _cache.CachedArtists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase))
                  ?? _cache.CachedArtists.FirstOrDefault(a => a.Name.Contains(artistName, StringComparison.OrdinalIgnoreCase));

        CurrentView = "MyMusic";
        IsNowPlayingActive = false;
        MyMusic.SelectedTabIndex = 1; // Artistas
        if (artist != null)
        {
            MyMusic.OpenArtistDetail(artist);
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
    public void OpenEditPlaylistDialog()
    {
        if (SelectedPlaylistForDetail == null) return;
        EditPlaylistName = SelectedPlaylistForDetail.Name;
        EditPlaylistDescription = SelectedPlaylistForDetail.Description;
        EditPlaylistCoverPath = SelectedPlaylistForDetail.CustomCoverPath;
        EditPlaylistCoverPreview = SelectedPlaylistForDetail.CoverImage;
        IsEditPlaylistDialogVisible = true;
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

    // Modal Track Properties State
    [ObservableProperty]
    private bool _isTrackPropertiesModalOpen;

    [ObservableProperty]
    private Track? _selectedTrackForProperties;

    [RelayCommand]
    public void ShowTrackProperties(Track? track)
    {
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
}
