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
    private Point _lastMousePosition = new(-1, -1);
    private bool _hasMousePositionInitialized = false;
    private long _lastFadeOutTick = 0;
    private long _manualHideGraceUntilTick = 0;

    public NowPlayingView()
    {
        InitializeComponent();

        _hudInactivityTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _hudInactivityTimer.Tick += OnInactivityTimerTick;

        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseDown += OnPreviewMouseDown;
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
    private static int _vlcRenderPending = 0;
    private WriteableBitmap? _vlcBitmap;

    private bool _isVlcActive = false;
    private bool _isMediaElementPlaying = false;
    private string? _currentVlcVideoPath = null;
    private bool _isCinemaMode = false;
    private bool _isVideoActive = false;
    private bool _hasVideoEndedForCurrentTrack = false;
    private double _knownVideoDurationSec = 0.0;
    private bool _isStoryboardActive = false;
    private int _storyboardLoadGeneration = 0;

    private static void EnsureLibVlcInitialized()
    {
        if (_isLibVlcInitialized) return;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new VlcInstance("--no-audio", "--avcodec-hw=any", "--clock-jitter=0");
            _libVlcPlayer = new VlcPlayer(_libVlc);
            _libVlcPlayer.EncounteredError += OnVlcPlayerEncounteredError;
            _libVlcPlayer.EndReached += OnVlcPlayerEndReached;
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

        // Limitar resolución máxima del buffer RV32 de software a 1280x720 (manteniendo proporción)
        // para reducir drásticamente el uso de CPU en conversión de color (swscale) y copia a WriteableBitmap.
        const uint maxWidth = 1280;
        const uint maxHeight = 720;
        if (width > maxWidth || height > maxHeight)
        {
            double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
            width = Math.Max(2u, ((uint)Math.Round(width * scale)) & ~1u);
            height = Math.Max(2u, ((uint)Math.Round(height * scale)) & ~1u);
        }

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
        // Evitar encolar múltiples delegados en el hilo UI si el frame anterior aún no se ha renderizado
        if (System.Threading.Interlocked.CompareExchange(ref _vlcRenderPending, 1, 0) != 0)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            System.Threading.Interlocked.Exchange(ref _vlcRenderPending, 0);
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _currentInstance?.RenderVlcFrame();
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _vlcRenderPending, 0);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
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
        if (!_isVlcActive || !IsVisible || _vlcBitmap == null || _vlcBuffer == IntPtr.Zero || _vlcVideoWidth <= 0 || _vlcVideoHeight <= 0) return;
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
                _libVlcPlayer.EndReached -= OnVlcPlayerEndReached;
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
        _hasVideoEndedForCurrentTrack = false;
        _knownVideoDurationSec = 0.0;
        StopOsuVideoPlayback();

        if (DataContext is MainViewModel vm &&
            vm.NowPlaying.PlayOsuBackgroundVideo &&
            track != null &&
            track.HasVideo &&
            !_incompatibleVideos.Contains(track.VideoPath) &&
            System.IO.File.Exists(track.VideoPath))
        {
            _isVideoActive = true;
            StopBackgroundCinematicMotion();
            if (!_isCinemaMode)
            {
                _isCinemaMode = true;
                UpdateCinemaModeVisualState(showToast: false);
            }
            if (OsuCinemaBlackBackdrop != null)
            {
                OsuCinemaBlackBackdrop.Visibility = Visibility.Visible;
            }
            if (OsuBackgroundVideoPlayer != null)
            {
                OsuBackgroundVideoPlayer.Source = new Uri(track.VideoPath, UriKind.Absolute);
                OsuBackgroundVideoPlayer.Play();
                _isMediaElementPlaying = true;
                OsuBackgroundVideoPlayer.Opacity = 1.0;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
                BackgroundTransformContainer.Visibility = Visibility.Collapsed;
            }
            UpdateVideoBlurBackdropVisibility();
        }
        else
        {
            _isVideoActive = false;
            if (_isCinemaMode && !_isStoryboardActive)
            {
                _isCinemaMode = false;
                UpdateCinemaModeVisualState(showToast: false);
            }
            RestoreArtworkBackground();
        }

        UpdateOsuStoryboardState(track);
    }

    private async void UpdateOsuStoryboardState(Track? track)
    {
        int gen = System.Threading.Interlocked.Increment(ref _storyboardLoadGeneration);
        StopOsuStoryboardPlayback();

        if (DataContext is not MainViewModel vm ||
            !vm.NowPlaying.PlayOsuStoryboard ||
            track == null ||
            string.IsNullOrWhiteSpace(track.FilePath))
        {
            if (!_isVideoActive)
            {
                if (_isCinemaMode)
                {
                    _isCinemaMode = false;
                    UpdateCinemaModeVisualState(showToast: false);
                }
                RestoreArtworkBackground();
            }
            return;
        }

        bool isOsuContext = track.IsOsuTrack ||
                            track.FilePath.IndexOf("Songs", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isOsuContext)
        {
            if (!_isVideoActive)
                RestoreArtworkBackground();
            return;
        }

        string audioPath = track.FilePath;
        var parsedStoryboard = await System.Threading.Tasks.Task.Run(() =>
            Services.Storyboard.OsuStoryboardParser.ParseFromTrackAudioPath(audioPath)).ConfigureAwait(true);

        if (gen != _storyboardLoadGeneration || !IsLoaded)
            return;

        if (parsedStoryboard != null && parsedStoryboard.HasRenderableSprites && OsuStoryboardLayer != null)
        {
            _isStoryboardActive = true;
            StopBackgroundCinematicMotion();

            // Fondo negro puro (#000000) cuando el storyboard se reproduce por sí solo (sin vídeo debajo)
            // y remover las imágenes estáticas de fondo / fotos de artista
            if (OsuCinemaBlackBackdrop != null)
            {
                OsuCinemaBlackBackdrop.Visibility = Visibility.Visible;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
                BackgroundTransformContainer.Visibility = Visibility.Collapsed;
            }

            OsuStoryboardLayer.UseSolidBlackBackground = !_isVideoActive;
            OsuStoryboardLayer.SetFpsModeFromString(vm.NowPlaying.OsuStoryboardFpsMode);
            OsuStoryboardLayer.StretchMode = vm.NowPlaying.OsuVideoStretch;
            OsuStoryboardLayer.Visibility = Visibility.Visible;
            OsuStoryboardLayer.Opacity = 1.0;
            OsuStoryboardLayer.LoadStoryboard(parsedStoryboard, vm.NowPlaying.CurrentPositionSeconds, vm.NowPlaying.IsPlaying);

            if (!_isCinemaMode)
            {
                _isCinemaMode = true;
                UpdateCinemaModeVisualState(showToast: false);
            }
        }
        else if (!_isVideoActive)
        {
            RestoreArtworkBackground();
        }
    }

    private void StopOsuStoryboardPlayback()
    {
        _isStoryboardActive = false;
        if (OsuStoryboardLayer != null)
        {
            OsuStoryboardLayer.UnloadStoryboard();
            OsuStoryboardLayer.Opacity = 0.0;
            OsuStoryboardLayer.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateVideoBlurBackdropVisibility()
    {
        if (OsuVideoBlurContainer == null) return;

        bool isUniform = DataContext is MainViewModel vm && vm.NowPlaying.OsuVideoStretch == Stretch.Uniform;
        if (_isVideoActive && isUniform)
        {
            OsuVideoBlurContainer.Visibility = Visibility.Visible;
            OsuVideoBlurContainer.Opacity = 1.0;
            if (OsuVideoMediaElementBlur != null)
                OsuVideoMediaElementBlur.Visibility = _isVlcActive ? Visibility.Collapsed : Visibility.Visible;
            if (OsuVlcVideoBlurImage != null)
                OsuVlcVideoBlurImage.Visibility = _isVlcActive ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            // En modo UniformToFill (o sin vídeo activo), el vídeo cubre toda la pantalla sin bordes:
            // colapsar la capa de desenfoque en tiempo real ahorra una pasada completa de shader por fotograma.
            OsuVideoBlurContainer.Opacity = 0.0;
            OsuVideoBlurContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void StopOsuVideoPlayback()
    {
        _isVideoActive = false;
        _isMediaElementPlaying = false;
        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.Stop();
            OsuBackgroundVideoPlayer.Source = null;
            OsuBackgroundVideoPlayer.Opacity = 0.0;
        }
        if (OsuVideoBlurContainer != null)
        {
            OsuVideoBlurContainer.Opacity = 0.0;
            OsuVideoBlurContainer.Visibility = Visibility.Collapsed;
        }
        if (OsuStoryboardLayer != null)
        {
            OsuStoryboardLayer.UseSolidBlackBackground = true;
        }
        if (BackgroundTransformContainer != null && !_isStoryboardActive)
        {
            BackgroundTransformContainer.Visibility = Visibility.Visible;
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

    /// <summary>
    /// Se invoca cuando un vídeo de osu! llega a su fin antes o al terminar la canción.
    /// Libera el decodificador de vídeo y limpia la escena dejando el fondo en negro puro / oscurecido.
    /// </summary>
    private void CompleteAndReleaseOsuVideo()
    {
        _hasVideoEndedForCurrentTrack = true;
        _isVideoActive = false;
        _isMediaElementPlaying = false;

        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.Stop();
            OsuBackgroundVideoPlayer.Source = null;
            OsuBackgroundVideoPlayer.Opacity = 0.0;
        }
        if (OsuVideoBlurContainer != null)
        {
            OsuVideoBlurContainer.Opacity = 0.0;
            OsuVideoBlurContainer.Visibility = Visibility.Collapsed;
        }
        StopVlcPlayback();

        if (OsuStoryboardLayer != null)
        {
            OsuStoryboardLayer.UseSolidBlackBackground = true;
        }

        // Mantener el fondo en negro / oscurecido al acabar el vídeo sin volver a cargar imágenes estáticas
        if (OsuCinemaBlackBackdrop != null)
        {
            OsuCinemaBlackBackdrop.Visibility = Visibility.Visible;
        }
        if (BackgroundTransformContainer != null)
        {
            BackgroundTransformContainer.Opacity = 0.0;
            BackgroundTransformContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void OnOsuVideoMediaEnded(object? sender, RoutedEventArgs e)
    {
        if (OsuBackgroundVideoPlayer != null && OsuBackgroundVideoPlayer.NaturalDuration.HasTimeSpan)
        {
            _knownVideoDurationSec = OsuBackgroundVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        }
        CompleteAndReleaseOsuVideo();
    }

    private static void OnVlcPlayerEndReached(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_currentInstance != null)
            {
                if (_libVlcPlayer != null && _libVlcPlayer.Length > 500)
                {
                    _currentInstance._knownVideoDurationSec = _libVlcPlayer.Length / 1000.0;
                }
                _currentInstance.CompleteAndReleaseOsuVideo();
            }
        }));
    }

    private void SyncOsuVideoPlayback()
    {
        if (DataContext is not MainViewModel vm || vm.NowPlaying.CurrentTrack == null) return;

        if (_isStoryboardActive && OsuStoryboardLayer != null)
        {
            OsuStoryboardLayer.SyncPlayback(vm.NowPlaying.CurrentPositionSeconds, vm.NowPlaying.IsPlaying);
        }

        var currentTrack = vm.NowPlaying.CurrentTrack;
        double targetSec = vm.NowPlaying.CurrentPositionSeconds + (currentTrack.VideoOffsetMs / 1000.0);
        if (targetSec < 0) targetSec = 0;

        // Si el vídeo ya terminó y se liberó, no sobrecargar el decodificador;
        // solo recargarlo si el usuario retrocedió manualmente en la barra de progreso dentro del rango del vídeo.
        if (_hasVideoEndedForCurrentTrack)
        {
            if (vm.NowPlaying.PlayOsuBackgroundVideo &&
                _knownVideoDurationSec > 1.0 &&
                targetSec < _knownVideoDurationSec - 1.5 &&
                currentTrack.HasVideo &&
                !_incompatibleVideos.Contains(currentTrack.VideoPath) &&
                System.IO.File.Exists(currentTrack.VideoPath))
            {
                UpdateOsuVideoState(currentTrack);
            }
            return;
        }

        if (!_isVideoActive) return;

        if (_incompatibleVideos.Contains(currentTrack.VideoPath))
        {
            StopOsuVideoPlayback();
            return;
        }

        // Si LibVLC está activo como fallback
        if (_isVlcActive && _libVlcPlayer != null)
        {
            if (_libVlcPlayer.Length > 500)
            {
                _knownVideoDurationSec = _libVlcPlayer.Length / 1000.0;
            }
            if (_knownVideoDurationSec > 0.5 && targetSec >= _knownVideoDurationSec - 0.15)
            {
                CompleteAndReleaseOsuVideo();
                return;
            }

            long targetMs = (long)(targetSec * 1000.0);
            if (vm.NowPlaying.IsPlaying)
            {
                if (_libVlcPlayer.Time >= 0 && Math.Abs(_libVlcPlayer.Time - targetMs) > 850)
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
                if (_libVlcPlayer.Time >= 0 && Math.Abs(_libVlcPlayer.Time - targetMs) > 850)
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

        if (OsuBackgroundVideoPlayer.NaturalDuration.HasTimeSpan)
        {
            _knownVideoDurationSec = OsuBackgroundVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        }
        if (_knownVideoDurationSec > 0.5 && targetSec >= _knownVideoDurationSec - 0.15)
        {
            CompleteAndReleaseOsuVideo();
            return;
        }

        if (vm.NowPlaying.IsPlaying)
        {
            var targetTime = TimeSpan.FromSeconds(targetSec);

            // Re-sincronizar solo si hay desvío mayor a 1.0s para evitar micro-seeks constantes de CPU
            if (Math.Abs((OsuBackgroundVideoPlayer.Position - targetTime).TotalSeconds) > 1.0)
            {
                OsuBackgroundVideoPlayer.Position = targetTime;
            }
            if (!_isMediaElementPlaying)
            {
                OsuBackgroundVideoPlayer.Play();
                _isMediaElementPlaying = true;
            }
        }
        else
        {
            if (_isMediaElementPlaying)
            {
                OsuBackgroundVideoPlayer.Pause();
                _isMediaElementPlaying = false;
            }
        }
    }

    private void OnOsuVideoMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        e.Handled = true;
        _isMediaElementPlaying = false;

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
            StopBackgroundCinematicMotion();
            if (!_isCinemaMode)
            {
                _isCinemaMode = true;
                UpdateCinemaModeVisualState(showToast: false);
            }

            if (OsuVlcVideoImage != null)
            {
                OsuVlcVideoImage.Opacity = 1.0;
            }
            if (OsuStoryboardLayer != null)
            {
                OsuStoryboardLayer.UseSolidBlackBackground = false;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
                BackgroundTransformContainer.Visibility = Visibility.Collapsed;
            }
            UpdateVideoBlurBackdropVisibility();

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
            OsuVideoBlurContainer.Visibility = Visibility.Collapsed;
        }
        if (OsuStoryboardLayer != null)
        {
            OsuStoryboardLayer.UseSolidBlackBackground = true;
        }
        if (_isStoryboardActive || _hasVideoEndedForCurrentTrack)
        {
            if (OsuCinemaBlackBackdrop != null)
            {
                OsuCinemaBlackBackdrop.Visibility = Visibility.Visible;
            }
            if (BackgroundTransformContainer != null)
            {
                BackgroundTransformContainer.Opacity = 0.0;
                BackgroundTransformContainer.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (OsuCinemaBlackBackdrop != null)
        {
            OsuCinemaBlackBackdrop.Visibility = Visibility.Collapsed;
        }
        if (BackgroundTransformContainer != null)
        {
            BackgroundTransformContainer.Visibility = Visibility.Visible;
            BackgroundTransformContainer.Opacity = 1.0;
        }
        ResetDarknessOverlays();
        if (DataContext is MainViewModel vm && vm.NowPlaying.BackgroundImage != null)
        {
            BackgroundPrimary.Source = vm.NowPlaying.BackgroundImage;
        }
        StartBackgroundCinematicMotion();
        UpdateResponsiveVisualizerLayout(ActualWidth, ActualHeight);
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
            OsuBackgroundVideoPlayer.MediaEnded -= OnOsuVideoMediaEnded;
            OsuBackgroundVideoPlayer.MediaEnded += OnOsuVideoMediaEnded;
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
        UpdateResponsiveVisualizerLayout(ActualWidth, ActualHeight);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (OsuBackgroundVideoPlayer != null)
        {
            OsuBackgroundVideoPlayer.MediaFailed -= OnOsuVideoMediaFailed;
            OsuBackgroundVideoPlayer.MediaEnded -= OnOsuVideoMediaEnded;
        }
        StopOsuStoryboardPlayback();
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

    private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && IsBlankSpaceClick(e.OriginalSource as DependencyObject))
        {
            bool canHideHud = true;
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsNowPlayingQueueExpanded || vm.NowPlaying?.IsCompactMode == true || vm.NowPlaying?.IsCustomizationFlyoutOpen == true)
                    canHideHud = false;
            }
            else if (DataContext is NowPlayingViewModel np && (np.IsCompactMode || np.IsCustomizationFlyoutOpen))
            {
                canHideHud = false;
            }

            if (canHideHud)
            {
                if (_isHudVisible)
                {
                    _hudInactivityTimer.Stop();
                    // Dar 2.6 segundos de margen tras ocultar con clic para poder sacar el mouse de la ventana/pantalla sin despertar el HUD
                    _manualHideGraceUntilTick = Environment.TickCount64 + 2600;
                    FadeOutHud();
                    return;
                }
                else
                {
                    _manualHideGraceUntilTick = 0;
                    OnUserActivity(sender, e);
                    return;
                }
            }
        }

        _manualHideGraceUntilTick = 0;
        OnUserActivity(sender, e);
    }

    private bool IsBlankSpaceClick(DependencyObject? source)
    {
        if (source == null) return false;

        DependencyObject? curr = source;
        while (curr != null && !ReferenceEquals(curr, this))
        {
            if (ReferenceEquals(curr, TopHudContainer) ||
                ReferenceEquals(curr, BottomHudControlsContainer) ||
                ReferenceEquals(curr, QueueExpandedContainer) ||
                ReferenceEquals(curr, TechBadgesPanel))
            {
                return false;
            }

            if (curr is System.Windows.Controls.Primitives.ButtonBase ||
                curr is Slider ||
                curr is ComboBox ||
                curr is ComboBoxItem ||
                curr is ListViewItem ||
                curr is System.Windows.Controls.Primitives.ScrollBar ||
                curr is MenuItem ||
                curr is TextBox)
            {
                return false;
            }

            curr = curr is Visual || curr is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(curr)
                : LogicalTreeHelper.GetParent(curr);
        }

        return true;
    }

    private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Point currentPos = e.GetPosition(this);
        if (!_hasMousePositionInitialized)
        {
            _lastMousePosition = currentPos;
            _hasMousePositionInitialized = true;
            return;
        }

        // Ignorar eventos PreviewMouseMove sintéticos generados por WPF cuando cambia IsHitTestVisible,
        // el layout se reacomoda durante FadeOutHud(), o el visualizador/slider se actualiza bajo el cursor quieto.
        double dx = Math.Abs(currentPos.X - _lastMousePosition.X);
        double dy = Math.Abs(currentPos.Y - _lastMousePosition.Y);
        if (dx < 3.0 && dy < 3.0)
        {
            return;
        }

        _lastMousePosition = currentPos;

        // Si el usuario acaba de ocultar el HUD haciendo clic en un espacio en blanco,
        // respetar el tiempo de gracia para que pueda retirar el cursor de la pantalla sin reactivar el HUD.
        if (!_isHudVisible && Environment.TickCount64 < _manualHideGraceUntilTick)
        {
            return;
        }

        // Pequeño margen tras iniciar FadeOutHud para evitar rebotes por cambios de layout simultáneos
        if (!_isHudVisible && Environment.TickCount64 - _lastFadeOutTick < 350 && dx < 12.0 && dy < 12.0)
        {
            return;
        }

        OnUserActivity(sender, e);
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
        _lastFadeOutTick = Environment.TickCount64;
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

        if (_isVideoActive || _isStoryboardActive)
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

        if (_isVideoActive || _isStoryboardActive)
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
        else if (e.PropertyName == nameof(NowPlayingViewModel.PlayOsuStoryboard) && DataContext is MainViewModel vmSb)
        {
            UpdateOsuStoryboardState(vmSb.NowPlaying.CurrentTrack);
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.OsuStoryboardFpsMode) && DataContext is MainViewModel vmSbFps)
        {
            OsuStoryboardLayer?.SetFpsModeFromString(vmSbFps.NowPlaying.OsuStoryboardFpsMode);
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.OsuVideoStretch))
        {
            UpdateVideoBlurBackdropVisibility();
            if (OsuStoryboardLayer != null && DataContext is MainViewModel vmStretch)
            {
                OsuStoryboardLayer.StretchMode = vmStretch.NowPlaying.OsuVideoStretch;
            }
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
        if (_isVideoActive || _isStoryboardActive)
        {
            // Cuando hay un vídeo o storyboard de osu! activo, se omiten las imágenes estáticas y fotos del artista
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
        UpdateResponsiveVisualizerLayout(ActualWidth, ActualHeight);
    }

    private int _currentMotionMode = 0;

    private void StopBackgroundCinematicMotion()
    {
        if (BackgroundScale == null || BackgroundTranslate == null) return;
        BackgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BackgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BackgroundTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        BackgroundTranslate.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void StartBackgroundCinematicMotion()
    {
        if (_isVideoActive || _isStoryboardActive) return;
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
        UpdateResponsiveVisualizerLayout(e.NewSize.Width, e.NewSize.Height);
        UpdateCompactTitleMarquee();
    }

    private void UpdateResponsiveVisualizerLayout(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        // 1. Visualizador Central y Visualizador Inferior Derecha (modo escritorio > 750px):
        //    - Ocultar cuando el ancho es menor a 820px o el alto es menor a 530px (para que no invada la carátula/título).
        //    - Reducir progresivamente de escala entre 820px y 1180px (de 0.55x a 1.0x).
        bool hideLargeVisualizers = width < 820 || height < 530;
        double largeScale = Math.Clamp((width - 820.0) / 360.0, 0.0, 1.0) * 0.45 + 0.55;
        if (height < 680)
        {
            double heightFactor = Math.Clamp((height - 530.0) / 150.0, 0.60, 1.0);
            largeScale = Math.Min(largeScale, heightFactor);
        }

        if (VisualizerCenterInner != null)
        {
            VisualizerCenterInner.Visibility = hideLargeVisualizers ? Visibility.Collapsed : Visibility.Visible;
        }
        if (VisualizerCenterScale != null && !hideLargeVisualizers)
        {
            VisualizerCenterScale.ScaleX = largeScale;
            VisualizerCenterScale.ScaleY = largeScale;
        }

        if (VisualizerRightInner != null)
        {
            VisualizerRightInner.Visibility = hideLargeVisualizers ? Visibility.Collapsed : Visibility.Visible;
        }
        if (VisualizerRightScale != null && !hideLargeVisualizers)
        {
            VisualizerRightScale.ScaleX = largeScale;
            VisualizerRightScale.ScaleY = largeScale;
        }

        // 2. Visualizador en InfoCancion (debajo de título/artista en modo escritorio):
        //    - Ocultar solo si el alto es muy reducido (< 490px).
        //    - Escalar suavemente entre 750px y 1020px (de 0.75x a 1.0x).
        bool hideInfoVisualizer = height < 490;
        double infoScale = Math.Clamp((width - 750.0) / 270.0, 0.0, 1.0) * 0.25 + 0.75;
        if (VisualizerInfoInner != null)
        {
            VisualizerInfoInner.Visibility = hideInfoVisualizer ? Visibility.Collapsed : Visibility.Visible;
        }
        if (VisualizerInfoScale != null && !hideInfoVisualizer)
        {
            VisualizerInfoScale.ScaleX = infoScale;
            VisualizerInfoScale.ScaleY = infoScale;
        }

        // 3. Visualizador en Modo Compacto / Móvil (<= 750px):
        //    - Ocultar si el alto < 530px para no saturar cuando la ventana es muy baja.
        //    - Reducir su escala (de 0.52x a 0.80x) según el ancho disponible.
        if (CompactVisualizerContainer != null)
        {
            bool hideCompactVisualizer = height < 530;
            CompactVisualizerContainer.Visibility = hideCompactVisualizer ? Visibility.Collapsed : Visibility.Visible;
            if (CompactVisualizerScale != null && !hideCompactVisualizer)
            {
                double compactScale = Math.Clamp((width - 480.0) / 270.0, 0.0, 1.0) * 0.28 + 0.52;
                CompactVisualizerScale.ScaleX = compactScale;
                CompactVisualizerScale.ScaleY = compactScale;
            }
        }
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

