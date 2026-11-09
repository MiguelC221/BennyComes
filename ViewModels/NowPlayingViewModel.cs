using System;
using System.IO;
using System.Windows.Media.Imaging;
using BenyComes.Models;
using BenyComes.Services;
using BenyComes.Services.Audio;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    public AudioVisualizerService Visualizer => AudioVisualizerService.Instance;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private string _title = "Sin pistas por ahora";

    [ObservableProperty]
    private string _artist = "Prueba a reproducir algo";

    [ObservableProperty]
    private string _album = "";

    [ObservableProperty]
    private bool _isFetchingAlbum;

    [ObservableProperty]
    private bool _isAlbumNotFound;

    [ObservableProperty]
    private bool _isScrobbling;

    [ObservableProperty]
    private bool _isScrobbled;

    [ObservableProperty]
    private string _artistAndAlbum = "Sin pistas por ahora";

    [ObservableProperty]
    private BitmapImage? _albumArt;

    [ObservableProperty]
    private BitmapImage? _backgroundImage;

    [ObservableProperty]
    private string _currentTimeText = "0:00";

    [ObservableProperty]
    private string _totalTimeText = "0:00";

    [ObservableProperty]
    private double _currentPositionSeconds = 0;

    [ObservableProperty]
    private double _totalDurationSeconds = 1;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private bool _isShuffleEnabled;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private float _volume = 0.85f;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isCompactMode;

    public string QueuePositionText
    {
        get
        {
            int count = _audio.Queue.Count;
            if (count == 0) return string.Empty;
            int idx = _audio.CurrentQueueIndex >= 0 ? _audio.CurrentQueueIndex + 1 : 1;
            return $"{idx} / {count}";
        }
    }

    public string AlbumFallbackTitle =>
        !string.IsNullOrWhiteSpace(Album) && !string.Equals(Album, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase)
            ? Album
            : (!string.IsNullOrWhiteSpace(Title) ? Title : "Reproducción en curso");

    [ObservableProperty]
    private AudioTechnicalInfo _technicalInfo = new();

    [ObservableProperty]
    private bool _useArtistBackground = true;

    [ObservableProperty]
    private bool _alwaysShowArtistImage;

    public bool HasAvailableArtistImage => AvailableArtistImages.Count > 0;

    private double _savedBlurBeforeArtist = 35.0;
    private double _savedDarknessBeforeArtist = 0.70;

    [ObservableProperty]
    private double _backgroundBlurRadius = 4.0;

    [ObservableProperty]
    private double _backgroundDarkness = 0.30;

    public double MaxBlurRadius => AlwaysShowArtistImage ? 4.0 : 80.0;
    public double MaxDarkness => AlwaysShowArtistImage ? 0.30 : 0.90;

    [ObservableProperty]
    private bool _isFetchingArtistImages;

    [ObservableProperty]
    private string? _artistImageNotificationMessage;

    [ObservableProperty]
    private bool _showArtistImageNotification;

    private System.Windows.Threading.DispatcherTimer? _notificationDismissTimer;

    [ObservableProperty]
    private int _artistImageIntervalSeconds = 35;

    [ObservableProperty]
    private bool _isArtistPhotoActive;

    // osu! Background Video & Storyboard playback
    [ObservableProperty]
    private bool _playOsuBackgroundVideo = true;

    [ObservableProperty]
    private bool _playOsuStoryboard = true;

    partial void OnPlayOsuStoryboardChanged(bool value)
    {
        _ = Data.LibraryDao.Instance.SetSettingAsync("osu_play_storyboard", value.ToString());
    }

    [ObservableProperty]
    private string _osuVideoStretchMode = "Ajustar (proporción original)"; // "Ajustar (proporción original)" (Uniform) por default o "Rellenar pantalla" (UniformToFill)

    public System.Windows.Media.Stretch OsuVideoStretch =>
        string.Equals(OsuVideoStretchMode, "Rellenar pantalla", StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Media.Stretch.UniformToFill
            : System.Windows.Media.Stretch.Uniform;

    partial void OnOsuVideoStretchModeChanged(string value)
    {
        OnPropertyChanged(nameof(OsuVideoStretch));
        _ = Data.LibraryDao.Instance.SetSettingAsync("osu_video_stretch", value);
    }

    public System.Collections.ObjectModel.ObservableCollection<string> OsuVideoStretchOptions { get; } = new()
    {
        "Ajustar (proporción original)",
        "Rellenar pantalla"
    };

    public bool HasOsuVideo => CurrentTrack != null && CurrentTrack.HasVideo && (MainViewModel.Instance?.Settings.IsOsuModeEnabled == true || CurrentTrack.Genre == "osu!");

    private bool _hasOsuStoryboard;
    public bool HasOsuStoryboard
    {
        get => _hasOsuStoryboard;
        private set => SetProperty(ref _hasOsuStoryboard, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<string> AvailableArtistImages { get; } = new();
    private int _currentArtistImageIndex = 0;
    private readonly System.Windows.Threading.DispatcherTimer _artistImageCycleTimer;

    // Multi-Artist Collaboration selection
    public System.Collections.ObjectModel.ObservableCollection<string> CollaborationArtists { get; } = new();

    [ObservableProperty]
    private string _selectedCollaborationArtist = "Todos";

    public bool HasCollaborationArtists => CollaborationArtists.Count > 1;

    partial void OnSelectedCollaborationArtistChanged(string value)
    {
        if (Services.Audio.AudioPlayerService.Instance.CurrentTrack is Track track)
        {
            _ = RefreshArtistBackgroundAsync(track.Artist);
        }

        if (MainViewModel.Instance?.IsArtistGalleryModalOpen == true 
            && !string.IsNullOrWhiteSpace(value) 
            && !value.Equals("Todos", StringComparison.OrdinalIgnoreCase))
        {
            if (MainViewModel.Instance.SelectedGalleryArtist != value)
            {
                MainViewModel.Instance.SelectedGalleryArtist = value;
            }
        }
    }

    [RelayCommand]
    public void NextArtistImage()
    {
        CycleNextArtistImage();
    }

    [RelayCommand]
    public void ForceUpdateStyle()
    {
        OnPropertyChanged(nameof(BackgroundBlurRadius));
        OnPropertyChanged(nameof(BackgroundDarkness));
        if (AvailableArtistImages.Count > 0 && _currentArtistImageIndex < AvailableArtistImages.Count)
        {
            var bmp = LoadImageFromDisk(AvailableArtistImages[_currentArtistImageIndex]);
            if (bmp != null)
            {
                OnBackgroundTransitionRequested?.Invoke(bmp, true);
                return;
            }
        }

        if (BackgroundImage != null)
        {
            OnBackgroundTransitionRequested?.Invoke(BackgroundImage, false);
        }
    }

    [ObservableProperty]
    private bool _isCustomizationFlyoutOpen;

    [RelayCommand]
    public void ToggleCustomizationFlyout()
    {
        IsCustomizationFlyoutOpen = !IsCustomizationFlyoutOpen;
    }

    [RelayCommand]
    public void CloseCustomizationFlyout()
    {
        IsCustomizationFlyoutOpen = false;
    }

    public bool IsVisualizerActive
    {
        get => Visualizer.Mode != VisualizerMode.None;
        set
        {
            if (value)
            {
                if (Visualizer.Mode == VisualizerMode.None)
                {
                    Visualizer.SelectedVisualizerMode = "Barras de Espectro";
                }
            }
            else
            {
                Visualizer.SelectedVisualizerMode = "Ninguno";
            }
            OnPropertyChanged(nameof(IsVisualizerActive));
            OnPropertyChanged(nameof(SelectedVisualizerMode));
        }
    }

    public string SelectedVisualizerMode
    {
        get => Visualizer.SelectedVisualizerMode;
        set
        {
            Visualizer.SelectedVisualizerMode = value;
            OnPropertyChanged(nameof(SelectedVisualizerMode));
            OnPropertyChanged(nameof(IsVisualizerActive));
            _ = Data.LibraryDao.Instance.SetSettingAsync("visualizer_mode", value);
        }
    }

    public string SelectedVisualizerPlacement
    {
        get => Visualizer.VisualizerPlacement;
        set
        {
            Visualizer.VisualizerPlacement = value;
            OnPropertyChanged(nameof(SelectedVisualizerPlacement));
            _ = Data.LibraryDao.Instance.SetSettingAsync("visualizer_placement", value);
        }
    }

    public event Action<BitmapImage, bool>? OnBackgroundTransitionRequested;

    public NowPlayingViewModel()
    {
        _audio.TrackChanged += OnTrackChanged;
        _audio.PlaybackStateChanged += OnPlaybackStateChanged;
        _audio.PositionChanged += OnPositionChanged;
        _audio.AudioModeChanged += (s, m) => UpdateTechnicalBadges();
        _audio.OutputDeviceChanged += (s, d) => UpdateTechnicalBadges();

        Volume = _audio.Volume;
        IsMuted = _audio.IsMuted;
        IsShuffle = _audio.IsShuffle;
        IsShuffleEnabled = _audio.IsShuffle;
        RepeatMode = _audio.RepeatMode;

        AlbumArt = MetadataService.Instance.GetFallbackArt();
        BackgroundImage = AlbumArt;
        UpdateTechnicalBadges();

        _artistImageCycleTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(ArtistImageIntervalSeconds)
        };
        _artistImageCycleTimer.Tick += (s, e) => CycleNextArtistImage();

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            string useBg = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_use_artist_bg", "true");
            UseArtistBackground = bool.TryParse(useBg, out var ub) ? ub : true;

            string alwaysShowVal = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_always_show_artist_img", "false");
            AlwaysShowArtistImage = bool.TryParse(alwaysShowVal, out var asVal) && asVal;

            string blurVal = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_blur_radius", "35");
            if (double.TryParse(blurVal, System.Globalization.CultureInfo.InvariantCulture, out var br))
                BackgroundBlurRadius = Math.Clamp(br, 0, 80);

            string savedBlurVal = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_saved_artist_blur", "");
            if (double.TryParse(savedBlurVal, System.Globalization.CultureInfo.InvariantCulture, out var sbr))
            {
                _savedArtistBlurRadius = sbr;
                _hasSavedArtistBlurRadius = true;
            }

            string darkVal = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_darkness", "0.70");
            if (double.TryParse(darkVal, System.Globalization.CultureInfo.InvariantCulture, out var dk))
            {
                BackgroundDarkness = Math.Clamp(dk, 0.0, 1.0);
                Visualizer.BackgroundDarkness = BackgroundDarkness;
            }

            string intVal = await Data.LibraryDao.Instance.GetSettingAsync("now_playing_img_interval", "35");
            if (int.TryParse(intVal, out var it))
            {
                ArtistImageIntervalSeconds = Math.Clamp(it, 15, 90);
                _artistImageCycleTimer.Interval = TimeSpan.FromSeconds(ArtistImageIntervalSeconds);
            }

            string placementVal = await Data.LibraryDao.Instance.GetSettingAsync("visualizer_placement", "InfoCancion");
            SelectedVisualizerPlacement = placementVal;

            string videoStretchVal = await Data.LibraryDao.Instance.GetSettingAsync("osu_video_stretch", "Ajustar (proporción original)");
            OsuVideoStretchMode = videoStretchVal;

            string playSbVal = await Data.LibraryDao.Instance.GetSettingAsync("osu_play_storyboard", "true");
            PlayOsuStoryboard = bool.TryParse(playSbVal, out var psb) ? psb : true;
        }
        catch { }
    }

    partial void OnAlwaysShowArtistImageChanged(bool value)
    {
        OnPropertyChanged(nameof(MaxBlurRadius));
        OnPropertyChanged(nameof(MaxDarkness));

        if (value)
        {
            // Limitar el blur hasta 4px y la oscuridad hasta el 30%
            if (BackgroundBlurRadius > 4.0)
            {
                _savedBlurBeforeArtist = BackgroundBlurRadius;
                BackgroundBlurRadius = 4.0;
            }
            if (BackgroundDarkness > 0.30)
            {
                _savedDarknessBeforeArtist = BackgroundDarkness;
                BackgroundDarkness = 0.30;
            }
        }
        else
        {
            // Quitar los límites y restaurar valores
            BackgroundBlurRadius = _savedBlurBeforeArtist > 4.0 ? _savedBlurBeforeArtist : 35.0;
            BackgroundDarkness = _savedDarknessBeforeArtist > 0.30 ? _savedDarknessBeforeArtist : 0.70;
        }

        _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_always_show_artist_img", value.ToString());
    }

    private double _savedArtistBlurRadius = 0.0;
    private bool _hasSavedArtistBlurRadius = false;

    partial void OnUseArtistBackgroundChanged(bool value)
    {
        _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_use_artist_bg", value.ToString());

        if (!value)
        {
            // Se desactivan las imágenes de artistas: guardamos el blur previo y subimos a 20
            _savedArtistBlurRadius = BackgroundBlurRadius;
            _hasSavedArtistBlurRadius = true;
            _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_saved_artist_blur", _savedArtistBlurRadius.ToString(System.Globalization.CultureInfo.InvariantCulture));
            BackgroundBlurRadius = 20.0;
        }
        else
        {
            // Se activan las imágenes de artistas: restauramos el blur a donde estaba
            if (_hasSavedArtistBlurRadius)
            {
                BackgroundBlurRadius = _savedArtistBlurRadius;
            }
        }

        _ = RefreshArtistBackgroundAsync(Artist);
    }

    partial void OnBackgroundBlurRadiusChanged(double value)
    {
        if (UseArtistBackground && AlwaysShowArtistImage && value > 4.0)
        {
            BackgroundBlurRadius = 4.0;
            return;
        }
        _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_blur_radius", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnBackgroundDarknessChanged(double value)
    {
        if (AlwaysShowArtistImage && value > 0.30)
        {
            BackgroundDarkness = 0.30;
            return;
        }
        Visualizer.BackgroundDarkness = value;
        _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_darkness", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnArtistImageIntervalSecondsChanged(int value)
    {
        _artistImageCycleTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(value, 15, 90));
        _ = Data.LibraryDao.Instance.SetSettingAsync("now_playing_img_interval", value.ToString());
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        CurrentTrack = track;

        if (track == null)
        {
            Title = "Sin pistas por ahora";
            Artist = "Prueba a reproducir algo";
            Album = "";
            ArtistAndAlbum = "Sin pistas por ahora";
            AlbumArt = MetadataService.Instance.GetFallbackArt();
            BackgroundImage = AlbumArt;
            CurrentTimeText = "0:00";
            TotalTimeText = "0:00";
            CurrentPositionSeconds = 0;
            TotalDurationSeconds = 1;
            HasOsuStoryboard = false;
            UpdateTechnicalBadges();
            return;
        }

        Title = track.Title;
        Artist = track.Artist;
        Album = track.Album;
        ArtistAndAlbum = track.ArtistAndAlbum;

        // Si la pista pertenece a osu!, extraer y guardar el fondo exacto desde cualquier .osu de la carpeta
        if (track.Genre == "osu!" || (!string.IsNullOrEmpty(track.FilePath) && track.FilePath.IndexOf("Songs", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            try
            {
                string? folder = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    string? osuBg = Services.Library.OsuLibraryService.ExtractBackgroundFromOsuFolder(folder);
                    if (!string.IsNullOrEmpty(osuBg) && File.Exists(osuBg))
                    {
                        track.ArtworkPath = osuBg;
                        var libTrack = Services.Library.OsuLibraryService.Instance.Tracks
                            .FirstOrDefault(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
                        if (libTrack != null)
                        {
                            libTrack.ArtworkPath = osuBg;
                        }
                    }
                }
            }
            catch { }
        }

        // Detener inmediatamente el ciclo de fotos de artistas y desmarcar foto de artista activa
        // para que las imágenes del artista anterior se remuevan al instante y no queden flotando
        _artistImageCycleTimer.Stop();
        AvailableArtistImages.Clear();
        _currentArtistImageIndex = 0;
        IsArtistPhotoActive = false;
        OnPropertyChanged(nameof(HasAvailableArtistImage));

        var art = MetadataService.Instance.GetHighResAlbumArt(track);
        AlbumArt = art;
        BackgroundImage = art;
        ThemeService.Instance.OnTrackArtChanged(art);

        // Solicitar transición suave de crossfade hacia la carátula o fondo del nuevo mapa/canción
        if (art != null)
        {
            OnBackgroundTransitionRequested?.Invoke(art, false);
        }

        TotalDurationSeconds = Math.Max(1, track.Duration.TotalSeconds);
        TotalTimeText = track.DurationFormatted;
        CurrentPositionSeconds = 0;
        CurrentTimeText = "0:00";

        UpdateTechnicalBadges();

        bool isOsuContext = track.IsOsuTrack ||
                            (!string.IsNullOrEmpty(track.FilePath) && track.FilePath.IndexOf("Songs", StringComparison.OrdinalIgnoreCase) >= 0);
        HasOsuStoryboard = isOsuContext && Services.Storyboard.OsuStoryboardParser.HasStoryboardInFolder(track.FilePath);

        OnPropertyChanged(nameof(HasOsuVideo));
        OnPropertyChanged(nameof(QueuePositionText));
        OnPropertyChanged(nameof(AlbumFallbackTitle));

        // Multi-artist collaboration detection
        CollaborationArtists.Clear();
        var split = Services.Library.ArtistSplitterService.Instance.SplitArtists(track.Artist);
        if (split.Count > 1)
        {
            CollaborationArtists.Add("Todos");
            foreach (var a in split)
            {
                if (!CollaborationArtists.Contains(a))
                    CollaborationArtists.Add(a);
            }
            SelectedCollaborationArtist = "Todos";
        }
        OnPropertyChanged(nameof(HasCollaborationArtists));

        _ = RefreshArtistBackgroundAsync(track.Artist);
    }

    public async Task RefreshArtistBackgroundAsync(string? artistName)
    {
        _artistImageCycleTimer.Stop();
        AvailableArtistImages.Clear();
        _currentArtistImageIndex = 0;
        OnPropertyChanged(nameof(HasAvailableArtistImage));

        if (!UseArtistBackground || string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
        {
            IsArtistPhotoActive = false;
            IsFetchingArtistImages = false;
            if (BackgroundImage != null)
            {
                OnBackgroundTransitionRequested?.Invoke(BackgroundImage, false);
            }
            return;
        }

        IsFetchingArtistImages = true;
        try
        {
            var imagesToUse = new List<string>();

            if (CollaborationArtists.Count > 1 && SelectedCollaborationArtist.Equals("Todos", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var artist in CollaborationArtists.Skip(1))
                {
                    var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(artist);
                    if (info.CachedImagePaths.Count > 0)
                    {
                        imagesToUse.AddRange(info.CachedImagePaths);
                    }
                }
            }
            else
            {
                string targetArtist = !string.IsNullOrWhiteSpace(SelectedCollaborationArtist) && !SelectedCollaborationArtist.Equals("Todos", StringComparison.OrdinalIgnoreCase)
                    ? SelectedCollaborationArtist
                    : artistName;

                var info = await Services.Online.LastFmService.Instance.GetArtistInfoAsync(targetArtist);
                if (info.CachedImagePaths.Count > 0)
                {
                    imagesToUse.AddRange(info.CachedImagePaths);
                }
            }

            foreach (var path in imagesToUse.Distinct())
            {
                AvailableArtistImages.Add(path);
            }
            OnPropertyChanged(nameof(HasAvailableArtistImage));

            if (AvailableArtistImages.Count > 0)
            {
                var firstBmp = LoadImageFromDisk(AvailableArtistImages[0]);
                if (firstBmp != null)
                {
                    IsArtistPhotoActive = true;
                    OnBackgroundTransitionRequested?.Invoke(firstBmp, true);

                    if (AvailableArtistImages.Count > 1)
                    {
                        _artistImageCycleTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(ArtistImageIntervalSeconds, 15, 90));
                        _artistImageCycleTimer.Start();
                    }
                    return;
                }
            }
            else
            {
                // Notificar que no existen imágenes de last.fm
                TriggerArtistNotification("No se encontraron imágenes en Last.fm para este artista");
            }
        }
        catch
        {
            TriggerArtistNotification("No se pudieron cargar imágenes de Last.fm");
        }
        finally
        {
            IsFetchingArtistImages = false;
        }

        // Fallback to album art
        IsArtistPhotoActive = false;
        if (BackgroundImage != null)
        {
            OnBackgroundTransitionRequested?.Invoke(BackgroundImage, false);
        }
    }

    private void TriggerArtistNotification(string message)
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            ArtistImageNotificationMessage = message;
            ShowArtistImageNotification = true;
            _notificationDismissTimer?.Stop();
            _notificationDismissTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _notificationDismissTimer.Tick += (s, e) =>
            {
                ShowArtistImageNotification = false;
                _notificationDismissTimer?.Stop();
            };
            _notificationDismissTimer.Start();
        });
    }

    private void CycleNextArtistImage()
    {
        if (!UseArtistBackground || AvailableArtistImages.Count == 0) return;

        _currentArtistImageIndex = (_currentArtistImageIndex + 1) % AvailableArtistImages.Count;
        var bmp = LoadImageFromDisk(AvailableArtistImages[_currentArtistImageIndex]);
        if (bmp != null)
        {
            IsArtistPhotoActive = true;
            OnBackgroundTransitionRequested?.Invoke(bmp, true);
        }
    }

    private static BitmapImage? LoadImageFromDisk(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        IsPlaying = state == PlaybackState.Playing;
    }

    [ObservableProperty]
    private bool _isUserSeeking;

    private void OnPositionChanged(object? sender, TimeSpan pos)
    {
        if (!IsUserSeeking)
        {
            CurrentPositionSeconds = pos.TotalSeconds;
            CurrentTimeText = pos.Hours > 0
                ? pos.ToString(@"h\:mm\:ss")
                : pos.ToString(@"m\:ss");
        }
    }

    private void UpdateTechnicalBadges()
    {
        TechnicalInfo.UpdateFromTrack(CurrentTrack, _audio.IsExclusiveMode, _audio.GetSelectedDeviceName());
    }

    [RelayCommand]
    private void PlayPause()
    {
        _audio.TogglePlayPause();
    }

    [RelayCommand]
    private void Next()
    {
        _audio.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _audio.Previous();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _audio.IsShuffle = !_audio.IsShuffle;
        IsShuffle = _audio.IsShuffle;
        IsShuffleEnabled = _audio.IsShuffle;
    }

    [RelayCommand]
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off
        };
        _audio.RepeatMode = RepeatMode;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _audio.IsMuted = !_audio.IsMuted;
        IsMuted = _audio.IsMuted;
    }

    [RelayCommand]
    public void Seek(double targetSeconds)
    {
        CurrentPositionSeconds = targetSeconds;
        var ts = TimeSpan.FromSeconds(targetSeconds);
        CurrentTimeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        _audio.Seek(ts);
    }

    partial void OnVolumeChanged(float value)
    {
        if (value > 0.001f && IsMuted)
        {
            IsMuted = false;
            _audio.IsMuted = false;
        }
        _audio.Volume = value;
    }
}
