using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BenyComes.Models;
using BenyComes.ViewModels;
using VlcInstance = LibVLCSharp.Shared.LibVLC;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcFromType = LibVLCSharp.Shared.FromType;

namespace BenyComes.Views;

public partial class NowPlayingView : UserControl
{
    private bool _isPrimaryActive = true;
    private readonly System.Windows.Threading.DispatcherTimer _hudInactivityTimer;
    private bool _isHudVisible = true;

    public NowPlayingView()
    {
        InitializeComponent();

        _hudInactivityTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _hudInactivityTimer.Tick += OnInactivityTimerTick;

        PreviewMouseMove += OnUserActivity;
        PreviewMouseDown += OnUserActivity;
        PreviewKeyDown += OnUserActivity;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        Services.Audio.AudioPlayerService.Instance.TrackChanged += OnAudioTrackChanged;

        if (Application.Current != null)
        {
            Application.Current.Exit -= OnApplicationExit;
            Application.Current.Exit += OnApplicationExit;
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartBackgroundCinematicMotion();
            if (DataContext is MainViewModel vm)
            {
                AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: true);
                if (vm.NowPlaying?.CurrentTrack != null)
                {
                    UpdateOsuVideoState(vm.NowPlaying.CurrentTrack);
                }
            }
            FadeInHud();
            _hudInactivityTimer.Stop();
            _hudInactivityTimer.Interval = _isCinemaMode ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3.5);
            _hudInactivityTimer.Start();
        }
        else
        {
            _hudInactivityTimer.Stop();
        }
    }

    private void OnAudioTrackChanged(object? sender, Track? track)
    {
        if (track == null) return;
        StartBackgroundCinematicMotion();
        PlayTrackChangedSlideAnimation();
        UpdateOsuVideoState(track);

        FadeInHud();
        if (_isCinemaMode)
        {
            _hudInactivityTimer.Stop();
            _hudInactivityTimer.Interval = TimeSpan.FromSeconds(10);
            _hudInactivityTimer.Start();
        }

        if (DataContext is MainViewModel vm && vm.IsNowPlayingQueueExpanded)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollToCurrentTrack()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void PlayTrackChangedSlideAnimation()
    {
        if (TrackInfoSlideX == null || TrackInfoContainer == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slideAnim = new DoubleAnimation(60.0, 0.0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = ease
        };
        var fadeAnim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = ease
        };

        TrackInfoSlideX.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        TrackInfoContainer.BeginAnimation(OpacityProperty, fadeAnim);
        UpdateCompactTitleMarquee();
    }

    // =========================================================================
    // ARQUITECTURA DE REPRODUCCIÓN DE VÍDEO DE FONDO (OSU! BEATMAPS):
    // -------------------------------------------------------------------------
    // Capa 1 - MediaElement (WPF nativo con Media Foundation):
    //   - MP4 (H.264)
    //   - AVI (MJPEG)
    //   - WMV
    //   - MOV básico
    //
    // Capa 2 - LibVLCSharp.WPF (Fallback transparente con motor LibVLC):
    //   - FLV
    //   - AVI (DivX/Xvid)
    //   - MPEG-1/2
    //   - MKV
    //   - WebM
    //   - Y el resto de formatos/códecs no soportados por Media Foundation
    // =========================================================================
    private static readonly HashSet<string> _incompatibleVideos = new(StringComparer.OrdinalIgnoreCase);
    private static bool _hasShownIncompatibleVideoToast = false;

    private static VlcInstance? _libVlc;
    private static VlcPlayer? _libVlcPlayer;
    private static bool _isLibVlcInitialized = false;
    private static NowPlayingView? _currentInstance;

    private static readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoFormatCb _videoFormatCb = VideoFormatCallback;
    private static readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoCleanupCb _videoCleanupCb = VideoCleanupCallback;
    private static readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoLockCb _videoLockCb = VideoLockCallback;
    private static readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoUnlockCb _videoUnlockCb = VideoUnlockCallback;
    private static readonly LibVLCSharp.Shared.MediaPlayer.LibVLCVideoDisplayCb _videoDisplayCb = VideoDisplayCallback;

    private static IntPtr _vlcBuffer = IntPtr.Zero;
    private static int _vlcBufferSize = 0;
    private static uint _vlcVideoWidth = 0;
    private static uint _vlcVideoHeight = 0;
    private WriteableBitmap? _vlcBitmap;

    private bool _isVlcActive = false;
    private string? _currentVlcVideoPath = null;
    private bool _isCinemaMode = false;
    private bool _isVideoActive = false;

    private static void EnsureLibVlcInitialized()
    {
        if (_isLibVlcInitialized) return;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new VlcInstance("--no-audio");
            _libVlcPlayer = new VlcPlayer(_libVlc);
            _libVlcPlayer.EncounteredError += OnVlcPlayerEncounteredError;
            _libVlcPlayer.SetVideoFormatCallbacks(_videoFormatCb, _videoCleanupCb);
            _libVlcPlayer.SetVideoCallbacks(_videoLockCb, _videoUnlockCb, _videoDisplayCb);
            _isLibVlcInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibVLC] Init failed: {ex.Message}");
        }
    }

    private static uint VideoFormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        byte[] fourcc = new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' };
        Marshal.Copy(fourcc, 0, chroma, 4);

        pitches = width * 4;
        lines = height;

        _vlcVideoWidth = width;
        _vlcVideoHeight = height;

        int newSize = (int)(pitches * lines);
        if (_vlcBuffer == IntPtr.Zero || _vlcBufferSize < newSize)
        {
            if (_vlcBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_vlcBuffer);
            }
            _vlcBuffer = Marshal.AllocHGlobal(newSize);
            _vlcBufferSize = newSize;
        }

        int w = (int)width;
        int h = (int)height;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _currentInstance?.SetupVlcBitmap(w, h);
        }));

        return 1;
    }

    private static void VideoCleanupCallback(ref IntPtr opaque)
    {
    }

    private static IntPtr VideoLockCallback(IntPtr opaque, IntPtr planes)
    {
        if (_vlcBuffer != IntPtr.Zero)
        {
            Marshal.WriteIntPtr(planes, _vlcBuffer);
        }
        return IntPtr.Zero;
    }

    private static void VideoUnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
    }

    private static void VideoDisplayCallback(IntPtr opaque, IntPtr picture)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _currentInstance?.RenderVlcFrame();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void SetupVlcBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_vlcBitmap == null || _vlcBitmap.PixelWidth != width || _vlcBitmap.PixelHeight != height)
        {
            _vlcBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            if (OsuVlcVideoImage != null)
            {
                OsuVlcVideoImage.Source = _vlcBitmap;
            }
            if (OsuVlcVideoBlurImage != null)
            {
                OsuVlcVideoBlurImage.Source = _vlcBitmap;
            }
        }
    }

    private void RenderVlcFrame()
    {
        if (!_isVlcActive || _vlcBitmap == null || _vlcBuffer == IntPtr.Zero || _vlcVideoWidth <= 0 || _vlcVideoHeight <= 0) return;
        try
        {
            int stride = (int)(_vlcVideoWidth * 4);
            _vlcBitmap.WritePixels(new Int32Rect(0, 0, _vlcBitmap.PixelWidth, _vlcBitmap.PixelHeight), _vlcBuffer, _vlcBufferSize, stride);
        }
        catch { }
    }

    private static void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        DisposeVlc();
    }

    private static void DisposeVlc()
    {
        try
        {
            if (_libVlcPlayer != null)
            {
                _libVlcPlayer.EncounteredError -= OnVlcPlayerEncounteredError;
                var oldMedia = _libVlcPlayer.Media;
                _libVlcPlayer.Media = null;
                oldMedia?.Dispose();
                _libVlcPlayer.Dispose();
                _libVlcPlayer = null;
            }

            _libVlc?.Dispose();
            _libVlc = null;
            _isLibVlcInitialized = false;

            if (_vlcBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_vlcBuffer);
                _vlcBuffer = IntPtr.Zero;
                _vlcBufferSize = 0;
            }
        }
        catch { }
    }

    private void UpdateOsuVideoState(Track? track)
    {
        StopOsuVideoPlayback();

        if (DataContext is MainViewModel vm &&
            vm.NowPlaying.PlayOsuBackgroundVideo &&
            track != null &&
            track.HasVideo &&
            !_incompatibleVideos.Contains(track.VideoPath) &&
            System.IO.File.Exists(track.VideoPath))
        {
            _isVideoActive = true;
            if (!_isCinemaMode)
            {
                _isCinemaMode = true;
                UpdateCinemaModeVisualState(showToast: false);
            }
            if (OsuBackgroundVideoPlayer != null)
            {
                OsuBackgroundVideoPlayer.Source = new Uri(track.VideoPath, UriKind.Absolute);
                OsuBackgroundVideoPlayer.Play();
                OsuBackgroundVideoPlayer.Opacity = 1.0;
            }
            if (OsuVideoBlurContainer != null)
            {
                OsuVideoBlurContainer.Opacity = 1.0;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
            }
        }
        else
        {
            _isVideoActive = false;
            if (_isCinemaMode)
            {
                _isCinemaMode = false;
                UpdateCinemaModeVisualState(showToast: false);
            }
            RestoreArtworkBackground();
        }
    }

    private void StopOsuVideoPlayback()
    {
        _isVideoActive = false;
        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.Stop();
            OsuBackgroundVideoPlayer.Source = null;
            OsuBackgroundVideoPlayer.Opacity = 0.0;
        }
        if (OsuVideoBlurContainer != null)
        {
            OsuVideoBlurContainer.Opacity = 0.0;
        }
        if (BackgroundTransformContainer != null)
        {
            BackgroundTransformContainer.Opacity = 1.0;
        }
        ResetDarknessOverlays();
        StopVlcPlayback();
    }

    private void StopVlcPlayback()
    {
        _isVlcActive = false;
        _currentVlcVideoPath = null;
        if (_libVlcPlayer != null)
        {
            _libVlcPlayer.Stop();
            var oldMedia = _libVlcPlayer.Media;
            _libVlcPlayer.Media = null;
            oldMedia?.Dispose();
        }
        if (OsuVlcVideoImage != null)
        {
            OsuVlcVideoImage.Opacity = 0.0;
            OsuVlcVideoImage.Source = null;
        }
        if (OsuVlcVideoBlurImage != null)
        {
            OsuVlcVideoBlurImage.Source = null;
        }
        _vlcBitmap = null;
    }

    private void SyncOsuVideoPlayback()
    {
        if (DataContext is not MainViewModel vm || vm.NowPlaying.CurrentTrack == null) return;

        var currentTrack = vm.NowPlaying.CurrentTrack;
        if (_incompatibleVideos.Contains(currentTrack.VideoPath))
        {
            StopOsuVideoPlayback();
            return;
        }

        double targetSec = vm.NowPlaying.CurrentPositionSeconds + (currentTrack.VideoOffsetMs / 1000.0);
        if (targetSec < 0) targetSec = 0;

        // Si LibVLC está activo como fallback
        if (_isVlcActive && _libVlcPlayer != null)
        {
            long targetMs = (long)(targetSec * 1000.0);
            if (vm.NowPlaying.IsPlaying)
            {
                if (_libVlcPlayer.Time >= 0 && Math.Abs(_libVlcPlayer.Time - targetMs) > 500)
                {
                    _libVlcPlayer.Time = targetMs;
                }
                if (!_libVlcPlayer.IsPlaying)
                {
                    _libVlcPlayer.Play();
                }
            }
            else
            {
                if (_libVlcPlayer.Time >= 0 && Math.Abs(_libVlcPlayer.Time - targetMs) > 500)
                {
                    _libVlcPlayer.Time = targetMs;
                }
                if (_libVlcPlayer.CanPause && _libVlcPlayer.IsPlaying)
                {
                    _libVlcPlayer.Pause();
                }
            }
            return;
        }

        // Si MediaElement está activo
        if (OsuBackgroundVideoPlayer == null || OsuBackgroundVideoPlayer.Source == null) return;

        if (vm.NowPlaying.IsPlaying)
        {
            var targetTime = TimeSpan.FromSeconds(targetSec);

            // Re-sincronizar solo si hay desvío mayor a 0.5s para no causar tartamudeo en el video
            if (Math.Abs((OsuBackgroundVideoPlayer.Position - targetTime).TotalSeconds) > 0.5)
            {
                OsuBackgroundVideoPlayer.Position = targetTime;
            }
            OsuBackgroundVideoPlayer.Play();
        }
        else
        {
            OsuBackgroundVideoPlayer.Pause();
        }
    }

    private void OnOsuVideoMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        e.Handled = true;

        string? failedPath = null;
        if (OsuBackgroundVideoPlayer?.Source != null && OsuBackgroundVideoPlayer.Source.IsFile)
        {
            failedPath = OsuBackgroundVideoPlayer.Source.LocalPath;
        }
        else if (DataContext is MainViewModel vmTrack && vmTrack.NowPlaying?.CurrentTrack != null)
        {
            failedPath = vmTrack.NowPlaying.CurrentTrack.VideoPath;
        }

        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.Stop();
            OsuBackgroundVideoPlayer.Source = null;
            OsuBackgroundVideoPlayer.Opacity = 0.0;
        }

        if (string.IsNullOrEmpty(failedPath) || !System.IO.File.Exists(failedPath))
        {
            RestoreArtworkBackground();
            return;
        }

        // Fallback a LibVLC
        if (TryPlayWithLibVlc(failedPath))
        {
            return;
        }

        // Si LibVLC tampoco pudo reproducirlo
        _incompatibleVideos.Add(failedPath);
        if (_isCinemaMode)
        {
            _isCinemaMode = false;
            UpdateCinemaModeVisualState(showToast: false);
        }
        RestoreArtworkBackground();
        ShowIncompatibleVideoToast(failedPath);
    }

    private bool TryPlayWithLibVlc(string videoPath)
    {
        EnsureLibVlcInitialized();
        if (_libVlc == null || _libVlcPlayer == null) return false;

        try
        {
            StopVlcPlayback();

            using var media = new VlcMedia(_libVlc, videoPath, VlcFromType.FromPath);
            _currentVlcVideoPath = videoPath;
            _isVlcActive = true;
            _isVideoActive = true;
            if (!_isCinemaMode)
            {
                _isCinemaMode = true;
                UpdateCinemaModeVisualState(showToast: false);
            }

            if (OsuVlcVideoImage != null)
            {
                OsuVlcVideoImage.Opacity = 1.0;
            }
            if (OsuVideoBlurContainer != null)
            {
                OsuVideoBlurContainer.Opacity = 1.0;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
            }

            _libVlcPlayer.Play(media);

            if (DataContext is MainViewModel vm && vm.NowPlaying.CurrentTrack != null)
            {
                double targetSec = vm.NowPlaying.CurrentPositionSeconds + (vm.NowPlaying.CurrentTrack.VideoOffsetMs / 1000.0);
                if (targetSec > 0)
                {
                    _libVlcPlayer.Time = (long)(targetSec * 1000.0);
                }
                if (!vm.NowPlaying.IsPlaying)
                {
                    _libVlcPlayer.Pause();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibVLC] Error starting video: {ex.Message}");
            StopVlcPlayback();
            return false;
        }
    }

    private static void OnVlcPlayerEncounteredError(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _currentInstance?.HandleVlcPlaybackFailure();
        }));
    }

    private void HandleVlcPlaybackFailure()
    {
        string? failedPath = _currentVlcVideoPath;
        if (!string.IsNullOrEmpty(failedPath))
        {
            _incompatibleVideos.Add(failedPath);
        }

        StopVlcPlayback();
        if (_isCinemaMode)
        {
            _isCinemaMode = false;
            UpdateCinemaModeVisualState(showToast: false);
        }
        RestoreArtworkBackground();
        ShowIncompatibleVideoToast(failedPath);
    }

    private void RestoreArtworkBackground()
    {
        _isVideoActive = false;
        if (OsuVideoBlurContainer != null)
        {
            OsuVideoBlurContainer.Opacity = 0.0;
        }
        if (BackgroundTransformContainer != null)
        {
            BackgroundTransformContainer.Opacity = 1.0;
        }
        ResetDarknessOverlays();
        if (DataContext is MainViewModel vm && vm.NowPlaying.BackgroundImage != null)
        {
            BackgroundPrimary.Source = vm.NowPlaying.BackgroundImage;
        }
        StartBackgroundCinematicMotion();
    }

    private void ResetDarknessOverlays()
    {
        double defaultDarkness = (DataContext is MainViewModel vm) ? vm.NowPlaying.BackgroundDarkness : 0.4;
        if (BackgroundDarknessOverlay != null)
        {
            BackgroundDarknessOverlay.BeginAnimation(OpacityProperty, null);
            BackgroundDarknessOverlay.Opacity = defaultDarkness;
        }
        if (BackgroundGradientOverlay != null)
        {
            BackgroundGradientOverlay.BeginAnimation(OpacityProperty, null);
            BackgroundGradientOverlay.Opacity = 1.0;
        }
    }

    private static void ShowIncompatibleVideoToast(string? videoPath)
    {
        if (!_hasShownIncompatibleVideoToast)
        {
            _hasShownIncompatibleVideoToast = true;
            string ext = !string.IsNullOrWhiteSpace(videoPath)
                ? System.IO.Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant()
                : string.Empty;

            string message = string.IsNullOrEmpty(ext)
                ? "Vídeo sin códec compatible, mostrando fondo de carátula"
                : $"Vídeo {ext} sin códec compatible, mostrando fondo de carátula";

            (MainViewModel.Instance)?.ShowToast(
                "osu! Video",
                message,
                isWarning: false,
                durationMs: 3500
            );
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _currentInstance = this;
        EnsureLibVlcInitialized();

        _hudInactivityTimer.Start();
        SizeChanged -= OnNowPlayingViewSizeChanged;
        SizeChanged += OnNowPlayingViewSizeChanged;

        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.MediaFailed -= OnOsuVideoMediaFailed;
            OsuBackgroundVideoPlayer.MediaFailed += OnOsuVideoMediaFailed;
        }

        if (CustomizationPopup != null)
        {
            CustomizationPopup.Opened -= OnCustomizationPopupOpened;
            CustomizationPopup.Opened += OnCustomizationPopupOpened;
        }

        if (DataContext is MainViewModel vm && vm.NowPlaying != null)
        {
            vm.NowPlaying.PropertyChanged -= OnNowPlayingPropertyChanged;
            vm.NowPlaying.PropertyChanged += OnNowPlayingPropertyChanged;
            vm.NowPlaying.OnBackgroundTransitionRequested -= OnBackgroundTransitionRequested;
            vm.NowPlaying.OnBackgroundTransitionRequested += OnBackgroundTransitionRequested;
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;

            if (vm.NowPlaying.BackgroundImage != null)
            {
                BackgroundPrimary.Source = vm.NowPlaying.BackgroundImage;
            }

            _ = vm.NowPlaying.RefreshArtistBackgroundAsync(vm.NowPlaying.Artist);
            AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: true);
            if (vm.NowPlaying.CurrentTrack != null)
            {
                UpdateOsuVideoState(vm.NowPlaying.CurrentTrack);
            }
        }

        StartBackgroundCinematicMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.MediaFailed -= OnOsuVideoMediaFailed;
        }
        StopVlcPlayback();
        if (ReferenceEquals(_currentInstance, this))
        {
            _currentInstance = null;
        }
    }

    private void OnCinemaModeButtonClick(object sender, RoutedEventArgs e)
    {
        _isCinemaMode = !_isCinemaMode;
        UpdateCinemaModeVisualState(showToast: true);
    }

    private void UpdateCinemaModeVisualState(bool showToast = false)
    {
        if (CinemaModeIcon != null && CinemaModeButton != null)
        {
            if (_isCinemaMode)
            {
                CinemaModeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.EyeOff24;
                if (DataContext is MainViewModel vm)
                {
                    CinemaModeIcon.Foreground = vm.Theme.CurrentAccentBrush;
                }
                CinemaModeButton.ToolTip = "Modo cine activado (Clic para desactivar)";
                if (showToast)
                {
                    (DataContext as MainViewModel ?? MainViewModel.Instance)?.ShowToast(
                        "Modo Cine",
                        "El HUD completo se ocultará automáticamente al dejar el cursor",
                        isWarning: false,
                        durationMs: 3000
                    );
                }
            }
            else
            {
                CinemaModeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24;
                CinemaModeIcon.Foreground = Brushes.White;
                CinemaModeButton.ToolTip = "Modo cine: ocultar todo el HUD al estar inactivo el cursor";
                FadeInHud();
            }
        }
    }

    private void OnUserActivity(object? sender, EventArgs e)
    {
        _hudInactivityTimer.Stop();
        _hudInactivityTimer.Interval = TimeSpan.FromSeconds(3.5);
        if (!_isHudVisible)
        {
            FadeInHud();
        }
        _hudInactivityTimer.Start();
    }

    private void OnInactivityTimerTick(object? sender, EventArgs e)
    {
        _hudInactivityTimer.Interval = TimeSpan.FromSeconds(3.5);
        if (_isDraggingSeek || _isDraggingVolume) return;
        if (DataContext is MainViewModel vm)
        {
            if (vm.IsNowPlayingQueueExpanded) return;
            if (vm.NowPlaying?.IsCompactMode == true) return;
        }
        else if (DataContext is NowPlayingViewModel np && np.IsCompactMode)
        {
            return;
        }

        if (_isHudVisible)
        {
            FadeOutHud();
        }
    }

    private void FadeOutHud()
    {
        _isHudVisible = false;
        var duration = TimeSpan.FromMilliseconds(500);
        var moveDuration = TimeSpan.FromMilliseconds(600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

        if (TopHudContainer != null)
        {
            TopHudContainer.BeginAnimation(OpacityProperty, fadeOut);
            TopHudContainer.IsHitTestVisible = false;
        }
        if (BottomHudControlsContainer != null)
        {
            BottomHudControlsContainer.BeginAnimation(OpacityProperty, fadeOut);
            BottomHudControlsContainer.IsHitTestVisible = false;
        }
        if (TechBadgesPanel != null)
        {
            TechBadgesPanel.BeginAnimation(OpacityProperty, fadeOut);
            TechBadgesPanel.IsHitTestVisible = false;
        }

        if (_isCinemaMode)
        {
            // Ocultar TODO el HUD completamente en Modo Cine
            if (TrackInfoContainer != null)
            {
                TrackInfoContainer.BeginAnimation(OpacityProperty, fadeOut);
                TrackInfoContainer.IsHitTestVisible = false;
            }
            if (VisualizerCenterContainer != null)
            {
                VisualizerCenterContainer.BeginAnimation(OpacityProperty, fadeOut);
                VisualizerCenterContainer.IsHitTestVisible = false;
            }
            if (VisualizerRightContainer != null)
            {
                VisualizerRightContainer.BeginAnimation(OpacityProperty, fadeOut);
                VisualizerRightContainer.IsHitTestVisible = false;
            }
            if (NormalImmersiveContainer != null)
            {
                NormalImmersiveContainer.BeginAnimation(OpacityProperty, fadeOut);
                NormalImmersiveContainer.IsHitTestVisible = false;
            }
        }
        else
        {
            // Al estar inactivo el mouse, acomodar suavemente hacia abajo el bloque único y los visualizadores alternativos
            double hudRowHeight = 0;
            if (StandardImmersiveLayout != null && StandardImmersiveLayout.RowDefinitions.Count > 1)
            {
                hudRowHeight = StandardImmersiveLayout.RowDefinitions[1].ActualHeight;
            }
            else if (BottomHudControlsContainer != null)
            {
                hudRowHeight = BottomHudControlsContainer.ActualHeight;
            }

            double trackLift = Math.Max(0, hudRowHeight - 28.0);
            if (TrackInfoTranslate != null)
            {
                TrackInfoTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(TrackInfoTranslate.Y, trackLift, moveDuration) { EasingFunction = easeInOut });
            }

            if (VisualizerCenterTranslate != null)
            {
                VisualizerCenterTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(VisualizerCenterTranslate.Y, trackLift, moveDuration) { EasingFunction = easeInOut });
            }
            if (VisualizerRightTranslate != null)
            {
                VisualizerRightTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(VisualizerRightTranslate.Y, trackLift, moveDuration) { EasingFunction = easeInOut });
            }
        }

        if (_isVideoActive)
        {
            var fadeDarkness = new DoubleAnimation(0.0, duration) { EasingFunction = ease };
            if (BackgroundDarknessOverlay != null)
            {
                BackgroundDarknessOverlay.BeginAnimation(OpacityProperty, fadeDarkness);
            }
            if (BackgroundGradientOverlay != null)
            {
                BackgroundGradientOverlay.BeginAnimation(OpacityProperty, fadeDarkness);
            }
        }
    }

    private void FadeInHud()
    {
        _isHudVisible = true;
        var duration = TimeSpan.FromMilliseconds(300);
        var returnDuration = TimeSpan.FromMilliseconds(450);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };

        if (_isVideoActive)
        {
            double targetDarkness = (DataContext is MainViewModel vmDark) ? vmDark.NowPlaying.BackgroundDarkness : 0.4;
            var restoreDarkness = new DoubleAnimation(targetDarkness, duration) { EasingFunction = ease };
            var restoreGradient = new DoubleAnimation(1.0, duration) { EasingFunction = ease };
            if (BackgroundDarknessOverlay != null)
            {
                BackgroundDarknessOverlay.BeginAnimation(OpacityProperty, restoreDarkness);
            }
            if (BackgroundGradientOverlay != null)
            {
                BackgroundGradientOverlay.BeginAnimation(OpacityProperty, restoreGradient);
            }
        }

        if (TopHudContainer != null)
        {
            TopHudContainer.BeginAnimation(OpacityProperty, fadeIn);
            TopHudContainer.IsHitTestVisible = true;
        }
        if (BottomHudControlsContainer != null)
        {
            BottomHudControlsContainer.BeginAnimation(OpacityProperty, fadeIn);
            BottomHudControlsContainer.IsHitTestVisible = true;
        }
        if (TechBadgesPanel != null)
        {
            TechBadgesPanel.BeginAnimation(OpacityProperty, fadeIn);
            TechBadgesPanel.IsHitTestVisible = true;
        }

        if (TrackInfoContainer != null)
        {
            TrackInfoContainer.BeginAnimation(OpacityProperty, fadeIn);
            TrackInfoContainer.IsHitTestVisible = true;
        }
        if (VisualizerCenterContainer != null)
        {
            VisualizerCenterContainer.BeginAnimation(OpacityProperty, fadeIn);
            VisualizerCenterContainer.IsHitTestVisible = true;
        }
        if (VisualizerRightContainer != null)
        {
            VisualizerRightContainer.BeginAnimation(OpacityProperty, fadeIn);
            VisualizerRightContainer.IsHitTestVisible = true;
        }
        if (NormalImmersiveContainer != null)
        {
            NormalImmersiveContainer.BeginAnimation(OpacityProperty, fadeIn);
            NormalImmersiveContainer.IsHitTestVisible = true;
        }

        // Restore track info container smoothly to original position
        if (TrackInfoTranslate != null)
        {
            var restoreTrackInfo = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
            TrackInfoTranslate.BeginAnimation(TranslateTransform.YProperty, restoreTrackInfo);
        }

        if (VisualizerCenterTranslate != null)
        {
            var restoreTrackInfo = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
            VisualizerCenterTranslate.BeginAnimation(TranslateTransform.YProperty, restoreTrackInfo);
        }
        if (VisualizerRightTranslate != null)
        {
            var restoreTrackInfo = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
            VisualizerRightTranslate.BeginAnimation(TranslateTransform.YProperty, restoreTrackInfo);
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsNowPlayingQueueExpanded) &&
            sender is MainViewModel vm)
        {
            AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: false);
        }
    }

    private void AnimateQueueState(bool isExpanded, bool immediate)
    {
        if (QueueExpandedContainer == null || NormalImmersiveContainer == null || QueueBackdropOverlay == null)
            return;

        if (immediate)
        {
            if (isExpanded)
            {
                NormalImmersiveContainer.Visibility = Visibility.Collapsed;
                NormalImmersiveContainer.Opacity = 0.0;
                QueueBackdropOverlay.Visibility = Visibility.Visible;
                QueueBackdropOverlay.Opacity = 0.9;
                QueueExpandedContainer.Visibility = Visibility.Visible;
                QueueExpandedContainer.Opacity = 1.0;
                QueueTranslate.Y = 0;
            }
            else
            {
                QueueExpandedContainer.Visibility = Visibility.Collapsed;
                QueueExpandedContainer.Opacity = 0.0;
                QueueBackdropOverlay.Visibility = Visibility.Collapsed;
                QueueBackdropOverlay.Opacity = 0.0;
                NormalImmersiveContainer.Visibility = Visibility.Visible;
                NormalImmersiveContainer.Opacity = 1.0;
                NormalImmersiveTranslate.Y = 0;
            }
            return;
        }

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (isExpanded)
        {
            FadeInHud();

            var fadeOutNormal = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
            fadeOutNormal.Completed += (s, e) => NormalImmersiveContainer.Visibility = Visibility.Collapsed;
            NormalImmersiveContainer.BeginAnimation(OpacityProperty, fadeOutNormal);

            QueueBackdropOverlay.Visibility = Visibility.Visible;
            QueueBackdropOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 0.9, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });

            QueueExpandedContainer.Visibility = Visibility.Visible;
            QueueExpandedContainer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut });
            QueueTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(80.0, 0.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut });

            Dispatcher.BeginInvoke(new Action(() => ScrollToCurrentTrack()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            // Ocultar cola animado: slide out downwards y fade out de la cola, y fade in suave de la vista normal
            var fadeOutQueue = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn };
            var slideOutQueue = new DoubleAnimation(0.0, 80.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn };
            fadeOutQueue.Completed += (s, e) =>
            {
                QueueExpandedContainer.Visibility = Visibility.Collapsed;
                QueueBackdropOverlay.Visibility = Visibility.Collapsed;
            };
            QueueExpandedContainer.BeginAnimation(OpacityProperty, fadeOutQueue);
            QueueTranslate.BeginAnimation(TranslateTransform.YProperty, slideOutQueue);

            QueueBackdropOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn });

            NormalImmersiveContainer.Visibility = Visibility.Visible;
            NormalImmersiveContainer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });
            NormalImmersiveTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-20.0, 0.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });
        }
    }

    private void OnLocateCurrentTrackClick(object sender, RoutedEventArgs e)
    {
        ScrollToCurrentTrack();
    }

    private void ScrollToCurrentTrack()
    {
        var current = Services.Audio.AudioPlayerService.Instance.CurrentTrack;
        if (current == null || QueueListView.Items.Count == 0) return;

        int index = -1;
        for (int i = 0; i < QueueListView.Items.Count; i++)
        {
            if (QueueListView.Items[i] is Track t && !string.IsNullOrEmpty(current.FilePath) && string.Equals(t.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        var sv = FindScrollViewer(QueueListView);
        if (sv != null)
        {
            if (sv.CanContentScroll)
            {
                // En desplazamiento lógico (CanContentScroll = true), vertical offset representa el índice del elemento.
                // Colocar vertical offset en 'index' posiciona la pista en la PRIMERA posición visible (tope de la pantalla).
                double maxOffset = Math.Max(0, sv.ScrollableHeight);
                double targetOffset = Math.Clamp(index, 0, maxOffset);
                sv.ScrollToVerticalOffset(targetOffset);
            }
            else
            {
                // En desplazamiento físico por píxeles, alinear el borde superior del elemento al tope del viewport
                QueueListView.ScrollIntoView(QueueListView.Items[index]);
                QueueListView.UpdateLayout();
                if (QueueListView.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
                {
                    try
                    {
                        var transform = item.TransformToAncestor(sv);
                        Point pt = transform.Transform(new Point(0, 0));
                        double itemAbsoluteTop = sv.VerticalOffset + pt.Y;
                        double maxOffset = Math.Max(0, sv.ScrollableHeight);
                        sv.ScrollToVerticalOffset(Math.Clamp(itemAbsoluteTop, 0, maxOffset));
                    }
                    catch
                    {
                        QueueListView.ScrollIntoView(QueueListView.Items[index]);
                    }
                }
            }
        }
        else
        {
            QueueListView.ScrollIntoView(QueueListView.Items[index]);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer s) return s;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private void OnBlurSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PrimaryBlur != null) PrimaryBlur.Radius = e.NewValue;
        if (SecondaryBlur != null) SecondaryBlur.Radius = e.NewValue;
    }

    private void OnDarknessSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundDarknessOverlay != null)
        {
            BackgroundDarknessOverlay.Opacity = e.NewValue;
        }
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundImage) && DataContext is MainViewModel vm)
        {
            if (!vm.NowPlaying.IsArtistPhotoActive && vm.NowPlaying.BackgroundImage != null)
            {
                CrossfadeBackgroundWithKenBurns(vm.NowPlaying.BackgroundImage, false);
            }
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundBlurRadius) && DataContext is MainViewModel vmBlur)
        {
            if (PrimaryBlur != null) PrimaryBlur.Radius = vmBlur.NowPlaying.BackgroundBlurRadius;
            if (SecondaryBlur != null) SecondaryBlur.Radius = vmBlur.NowPlaying.BackgroundBlurRadius;
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.PlayOsuBackgroundVideo) && DataContext is MainViewModel vmVideo)
        {
            UpdateOsuVideoState(vmVideo.NowPlaying.CurrentTrack);
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.CurrentPositionSeconds) ||
                 e.PropertyName == nameof(NowPlayingViewModel.IsPlaying))
        {
            SyncOsuVideoPlayback();
        }
    }

    private void OnBackgroundTransitionRequested(BitmapImage newImage, bool isArtistPhoto)
    {
        CrossfadeBackgroundWithKenBurns(newImage, isArtistPhoto);
    }

    private void CrossfadeBackgroundWithKenBurns(BitmapImage newImage, bool isArtistPhoto)
    {
        if (_isVideoActive && isArtistPhoto)
        {
            // Cuando hay un vídeo de osu! activo, desactiva automáticamente la imagen del artista
            return;
        }

        var duration = TimeSpan.FromMilliseconds(1000);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        double targetBlur = (DataContext is MainViewModel vm) ? vm.NowPlaying.BackgroundBlurRadius : 0;

        if (_isPrimaryActive)
        {
            BackgroundSecondary.Source = newImage;
            SecondaryBlur.Radius = targetBlur;

            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                if (!_isPrimaryActive)
                {
                    BackgroundPrimary.Source = null;
                }
            };

            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = false;
        }
        else
        {
            BackgroundPrimary.Source = newImage;
            PrimaryBlur.Radius = targetBlur;

            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                if (_isPrimaryActive)
                {
                    BackgroundSecondary.Source = null;
                }
            };

            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = true;
        }

        StartBackgroundCinematicMotion();
    }

    private int _currentMotionMode = 0;

    private void StartBackgroundCinematicMotion()
    {
        if (BackgroundScale == null || BackgroundTranslate == null) return;

        // Alternar entre los 4 modos cinemáticos direccionales tipo documental (Pan & Scan)
        _currentMotionMode = (_currentMotionMode + 1) % 4;

        // Velocidad ajustada: reducida a ~0.8 (duración extendida de 28s a 35s para un movimiento más suave y pausado)
        var duration = TimeSpan.FromSeconds(35);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        double fromX = 0, toX = 0;
        double fromY = 0, toY = 0;
        double fromScale = 1.04, toScale = 1.12;

        switch (_currentMotionMode)
        {
            case 0:
                // Modo 1: Paneo Panorámico Horizontal (Izquierda a Derecha)
                // Inicia encuadrando el extremo izquierdo y recorre lentamente hacia la derecha
                fromX = -52.0;
                toX = 52.0;
                fromY = -10.0;
                toY = 10.0;
                fromScale = 1.05;
                toScale = 1.12;
                break;

            case 1:
                // Modo 2: Recorrido Vertical (De arriba -zona de rostros o arte superior- hacia abajo con Zoom)
                // Permite apreciar los rostros al inicio y desciende para revelar toda la escena o carátula
                fromX = 10.0;
                toX = -10.0;
                fromY = -42.0;
                toY = 38.0;
                fromScale = 1.04;
                toScale = 1.13;
                break;

            case 2:
                // Modo 3: Paneo Horizontal Inverso con Zoom Out sutil (Derecha a Izquierda)
                fromX = 52.0;
                toX = -52.0;
                fromY = 11.0;
                toY = -11.0;
                fromScale = 1.14;
                toScale = 1.05;
                break;

            case 3:
                // Modo 4: Diagonal Cinemático de Esquina a Esquina (Superior izquierda a inferior derecha)
                fromX = -44.0;
                toX = 44.0;
                fromY = -34.0;
                toY = 30.0;
                fromScale = 1.04;
                toScale = 1.14;
                break;
        }

        var scaleAnim = new DoubleAnimation(fromScale, toScale, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        var panXAnim = new DoubleAnimation(fromX, toX, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        var panYAnim = new DoubleAnimation(fromY, toY, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        BackgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        BackgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        BackgroundTranslate.BeginAnimation(TranslateTransform.XProperty, panXAnim);
        BackgroundTranslate.BeginAnimation(TranslateTransform.YProperty, panYAnim);
    }

    private bool _isDraggingSeek;
    private bool _isDraggingVolume;

    private static void SetSliderValueFromMousePosition(Slider slider, System.Windows.Input.MouseEventArgs e)
    {
        if (slider.ActualWidth <= 0) return;
        Point pt = e.GetPosition(slider);
        double ratio = Math.Clamp(pt.X / slider.ActualWidth, 0.0, 1.0);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
    }

    private void OnSeekContainerMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NowPlayingSeekSlider != null && DataContext is MainViewModel vm)
        {
            _isDraggingSeek = true;
            vm.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
        }
    }

    private void OnSeekContainerMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && NowPlayingSeekSlider != null)
        {
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
        }
    }

    private void OnSeekContainerMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingSeek && NowPlayingSeekSlider != null && DataContext is MainViewModel vm)
        {
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
            _isDraggingSeek = false;
            vm.NowPlaying.SeekCommand.Execute(NowPlayingSeekSlider.Value);
            vm.NowPlaying.IsUserSeeking = false;
        }
    }

    private void OnSeekSliderPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            _isDraggingSeek = true;
            vm.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingSeek = false;
            vm.NowPlaying.SeekCommand.Execute(slider.Value);
            vm.NowPlaying.IsUserSeeking = false;
        }
    }

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is MainViewModel vm && sender is Slider slider && (slider.IsMouseCaptureWithin || vm.NowPlaying.IsUserSeeking || _isDraggingSeek))
        {
            var ts = TimeSpan.FromSeconds(e.NewValue);
            vm.NowPlaying.CurrentTimeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    private void OnVolumeSliderPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _isDraggingVolume = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingVolume && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingVolume = false;
        }
    }

    private void OnMoreOptionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.ContextMenu != null)
        {
            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.SelectedTrackForContext = Services.Audio.AudioPlayerService.Instance.CurrentTrack;
            }
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnTrackContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm && cm.DataContext is Models.Track track && MainViewModel.Instance != null)
        {
            MainViewModel.Instance.SelectedTrackForContext = track;
        }
    }

    private void OnAddToPlaylistSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem parentItem)
        {
            MainWindow.PopulateAddToPlaylistMenu(parentItem);
        }
    }

    private void OnVolumeAreaPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            float step = e.Delta > 0 ? 0.03f : -0.03f;
            float newVol = Math.Clamp(vm.NowPlaying.Volume + step, 0.0f, 1.0f);
            vm.NowPlaying.Volume = newVol;
            e.Handled = true;
        }
    }

    private void OnQueueItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(dep) != null)
        {
            return;
        }

        Track? track = null;
        if (sender is ListViewItem lvi && lvi.DataContext is Track t1)
        {
            track = t1;
        }
        else if (sender is ListView lv && lv.SelectedItem is Track t2)
        {
            track = t2;
        }
        else if (e.OriginalSource is DependencyObject depObj)
        {
            track = FindVisualParent<ListViewItem>(depObj)?.DataContext as Track;
        }

        if (track == null && DataContext is MainViewModel vm0)
        {
            track = vm0.Queue.SelectedTrack;
        }

        if (track != null && DataContext is MainViewModel vm)
        {
            vm.Queue.PlayTrackCommand.Execute(track);
        }
    }

    private void OnCustomizationPopupOpened(object? sender, EventArgs e)
    {
        UpdateCustomizationFlyoutMaxHeight();
    }

    private void OnNowPlayingViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCustomizationFlyoutMaxHeight();
        bool isCompact = e.NewSize.Width <= 750;
        if (DataContext is MainViewModel vm && vm.NowPlaying != null)
        {
            if (vm.NowPlaying.IsCompactMode != isCompact)
                vm.NowPlaying.IsCompactMode = isCompact;
        }
        else if (DataContext is NowPlayingViewModel np)
        {
            if (np.IsCompactMode != isCompact)
                np.IsCompactMode = isCompact;
        }
        UpdateCompactTitleMarquee();
    }

    private void OnCompactTitleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactTitleMarquee();
    }

    private void UpdateCompactTitleMarquee()
    {
        if (CompactTitleViewport == null || CompactTitleTextBlock == null || CompactTitleTranslate == null) return;

        CompactTitleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CompactTitleTranslate.X = 0;

        double viewportWidth = CompactTitleViewport.ActualWidth;
        double textWidth = CompactTitleTextBlock.ActualWidth;
        double overflow = textWidth - viewportWidth;

        if (viewportWidth > 10 && overflow > 2.0)
        {
            double scrollSeconds = Math.Max(3.0, overflow / 24.0);
            double pauseSeconds = 2.0;

            var keyFrames = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            double t0 = 0;
            double t1 = pauseSeconds;
            double t2 = t1 + scrollSeconds;
            double t3 = t2 + pauseSeconds;
            double t4 = t3 + scrollSeconds;

            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t0)));
            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t1)));
            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, TimeSpan.FromSeconds(t2)));
            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, TimeSpan.FromSeconds(t3)));
            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t4)));

            CompactTitleTranslate.BeginAnimation(TranslateTransform.XProperty, keyFrames);
        }
    }

    private void UpdateCustomizationFlyoutMaxHeight()
    {
        if (CustomizationScrollViewer == null) return;
        double availableHeight = ActualHeight;
        if (availableHeight <= 0)
        {
            var win = Window.GetWindow(this);
            if (win != null) availableHeight = win.ActualHeight;
        }

        // Dejar margen superior e inferior (~120px) para que el flyout no desborde la ventana
        double maxContentHeight = Math.Max(180, availableHeight - 120);
        CustomizationScrollViewer.MaxHeight = maxContentHeight;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}

