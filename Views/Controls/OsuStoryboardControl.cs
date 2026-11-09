using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BenyComes.Services.Storyboard;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace BenyComes.Views.Controls;

public enum StoryboardFpsMode
{
    Fps60 = 0,
    Fps30 = 1,
    VSync = 2
}

/// <summary>
/// Lienzo 2D acelerado por SkiaSharp para renderizar Storyboards de osu! (Nivel 2: Avanzado).
/// Soporta Sprite, Animation (spritesheets por frames), coordenadas Widescreen (WidescreenStoryboard: 0/1),
/// recorte de cámara virtual y pre-carga asíncrona de texturas.
/// </summary>
public sealed class OsuStoryboardControl : SKElement
{
    private OsuStoryboard? _storyboard;
    private readonly ConcurrentDictionary<string, SKBitmap?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, SKColorFilter> _colorFilterCache = new();
    private int _preloadGeneration;
    private readonly DispatcherTimer _renderTimer;
    private readonly Stopwatch _clockInterpolator = new();
    private readonly SKPaint _spritePaint = new()
    {
        IsAntialias = true,
        FilterQuality = SKFilterQuality.Low
    };

    private double _anchorAudioTimeMs;
    private bool _isPlaying;
    private bool _isVsyncSubscribed;
    private bool _hasStoryboardFinished;
    private TimeSpan _lastVsyncRenderTime = TimeSpan.Zero;
    private Stretch _stretchMode = Stretch.UniformToFill;
    private StoryboardFpsMode _fpsMode = StoryboardFpsMode.Fps60;
    private bool _useSolidBlackBackground = true;

    public bool HasActiveStoryboard => _storyboard != null && _storyboard.HasRenderableSprites;

    /// <summary>
    /// Cuando es true (por defecto al no haber vídeo debajo), limpia el lienzo en negro puro (#000000)
    /// para que el storyboard avance por sí solo sin imágenes de fondo ajenas.
    /// </summary>
    public bool UseSolidBlackBackground
    {
        get => _useSolidBlackBackground;
        set
        {
            if (_useSolidBlackBackground != value)
            {
                _useSolidBlackBackground = value;
                if (HasActiveStoryboard && IsVisible)
                {
                    InvalidateVisual();
                }
            }
        }
    }

    public StoryboardFpsMode FpsMode
    {
        get => _fpsMode;
        set
        {
            if (_fpsMode != value)
            {
                _fpsMode = value;
                ApplyFpsTimerInterval();
                UpdateTimerState();
            }
        }
    }

    public void SetFpsModeFromString(string? modeStr)
    {
        if (string.IsNullOrWhiteSpace(modeStr))
        {
            FpsMode = StoryboardFpsMode.Fps60;
            return;
        }

        if (modeStr.Contains("30", StringComparison.OrdinalIgnoreCase))
            FpsMode = StoryboardFpsMode.Fps30;
        else if (modeStr.Contains("VSync", StringComparison.OrdinalIgnoreCase) ||
                 modeStr.Contains("Sincronización", StringComparison.OrdinalIgnoreCase) ||
                 modeStr.Contains("Sincronizacion", StringComparison.OrdinalIgnoreCase))
            FpsMode = StoryboardFpsMode.VSync;
        else
            FpsMode = StoryboardFpsMode.Fps60;
    }

    public Stretch StretchMode
    {
        get => _stretchMode;
        set
        {
            if (_stretchMode != value)
            {
                _stretchMode = value;
                if (HasActiveStoryboard && IsVisible)
                {
                    InvalidateVisual();
                }
            }
        }
    }

    public OsuStoryboardControl()
    {
        IsHitTestVisible = false;
        IgnorePixelScaling = true; // Renderizar a escala de píxeles lógicos de WPF para ahorrar fillrate y CPU/GPU
        CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = true }; // Composición acelerada por GPU en DirectX/WPF

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16.6667) // 60 FPS por defecto
        };
        _renderTimer.Tick += OnRenderTimerTick;

        IsVisibleChanged += (_, _) => UpdateTimerState();
        Unloaded += (_, _) => StopRenderTimer();
    }

    private void ApplyFpsTimerInterval()
    {
        _renderTimer.Interval = _fpsMode switch
        {
            StoryboardFpsMode.Fps30 => TimeSpan.FromMilliseconds(33.3333), // 30 FPS
            _ => TimeSpan.FromMilliseconds(16.6667)                        // 60 FPS
        };
    }

    public void LoadStoryboard(OsuStoryboard? storyboard, double currentPositionSeconds, bool isPlaying)
    {
        UnloadStoryboard();

        _storyboard = storyboard;
        _anchorAudioTimeMs = currentPositionSeconds * 1000.0;
        _isPlaying = isPlaying;
        _hasStoryboardFinished = false;

        if (_storyboard != null && _storyboard.HasRenderableSprites)
        {
            int gen = Interlocked.Increment(ref _preloadGeneration);
            var sbRef = _storyboard;
            _ = Task.Run(() => PreloadTexturesInBackground(sbRef, gen));

            if (_isPlaying)
                _clockInterpolator.Restart();
            else
                _clockInterpolator.Reset();

            UpdateTimerState();
            InvalidateVisual();
        }
    }

    private void PreloadTexturesInBackground(OsuStoryboard sb, int gen)
    {
        try
        {
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sp in sb.Sprites)
            {
                if (sp.IsAnimation && sp.AnimationFramePaths != null)
                {
                    foreach (var fp in sp.AnimationFramePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(fp))
                            uniquePaths.Add(fp);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(sp.RelativeFilePath))
                {
                    uniquePaths.Add(sp.RelativeFilePath);
                }
            }

            foreach (var relPath in uniquePaths)
            {
                if (gen != Volatile.Read(ref _preloadGeneration) || _hasStoryboardFinished)
                    return;
                GetOrLoadTexture(sb.FolderPath, relPath);
            }
        }
        catch { }
    }

    public void SyncPlayback(double currentPositionSeconds, bool isPlaying)
    {
        double targetMs = currentPositionSeconds * 1000.0;
        double currentEstimatedMs = GetInterpolatedTimeMs();

        // Si el storyboard había terminado pero el usuario retrocedió en la barra de progreso, reactivarlo
        if (_hasStoryboardFinished && _storyboard != null && targetMs <= _storyboard.MaxEndTimeMs)
        {
            _hasStoryboardFinished = false;
        }

        // Sincronizar reloj interpolado si hubo seek o deriva > 90 ms, o si cambió el estado Play/Pause
        if (_isPlaying != isPlaying || Math.Abs(currentEstimatedMs - targetMs) > 90.0)
        {
            _anchorAudioTimeMs = targetMs;
            if (isPlaying)
                _clockInterpolator.Restart();
            else
                _clockInterpolator.Reset();
        }

        _isPlaying = isPlaying;
        UpdateTimerState();

        if (!_isPlaying && HasActiveStoryboard && IsVisible && !_hasStoryboardFinished)
        {
            InvalidateVisual();
        }
    }

    private void ClearTextureCache()
    {
        Interlocked.Increment(ref _preloadGeneration);
        foreach (var kv in _textureCache)
        {
            if (_textureCache.TryRemove(kv.Key, out var bmp))
            {
                bmp?.Dispose();
            }
        }
        _textureCache.Clear();

        foreach (var filter in _colorFilterCache.Values)
        {
            filter.Dispose();
        }
        _colorFilterCache.Clear();
    }

    public void UnloadStoryboard()
    {
        StopRenderTimer();
        _clockInterpolator.Reset();
        _storyboard = null;
        _hasStoryboardFinished = false;
        ClearTextureCache();
        InvalidateVisual();
    }

    private double GetInterpolatedTimeMs()
    {
        if (!_isPlaying)
            return _anchorAudioTimeMs;
        return _anchorAudioTimeMs + _clockInterpolator.Elapsed.TotalMilliseconds;
    }

    private void UpdateTimerState()
    {
        bool shouldRun = HasActiveStoryboard && !_hasStoryboardFinished && IsVisible && _isPlaying && Visibility == Visibility.Visible && Opacity > 0.01;
        if (!shouldRun)
        {
            StopRenderTimer();
            return;
        }

        if (_fpsMode == StoryboardFpsMode.VSync)
        {
            if (_renderTimer.IsEnabled)
                _renderTimer.Stop();

            if (!_isVsyncSubscribed)
            {
                CompositionTarget.Rendering += OnCompositionTargetRendering;
                _isVsyncSubscribed = true;
            }
        }
        else
        {
            if (_isVsyncSubscribed)
            {
                CompositionTarget.Rendering -= OnCompositionTargetRendering;
                _isVsyncSubscribed = false;
            }

            ApplyFpsTimerInterval();
            if (!_renderTimer.IsEnabled)
            {
                _renderTimer.Start();
            }
        }
    }

    private void StopRenderTimer()
    {
        if (_renderTimer.IsEnabled)
        {
            _renderTimer.Stop();
        }
        if (_isVsyncSubscribed)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isVsyncSubscribed = false;
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (!HasActiveStoryboard || !IsVisible || Visibility != Visibility.Visible || !_isPlaying)
        {
            StopRenderTimer();
            return;
        }

        if (e is RenderingEventArgs rea)
        {
            if (rea.RenderingTime == _lastVsyncRenderTime)
                return;
            _lastVsyncRenderTime = rea.RenderingTime;
        }

        InvalidateVisual();
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        if (!HasActiveStoryboard || !IsVisible || Visibility != Visibility.Visible)
        {
            StopRenderTimer();
            return;
        }
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(_useSolidBlackBackground ? SKColors.Black : SKColors.Transparent);

        if (_storyboard == null || !_storyboard.HasRenderableSprites)
            return;

        int viewW = e.Info.Width;
        int viewH = e.Info.Height;
        if (viewW <= 0 || viewH <= 0) return;

        double timeMs = GetInterpolatedTimeMs();

        // Si el storyboard ya superó su último evento activo, liberar las texturas de memoria,
        // detener el bucle de renderizado y dejar el lienzo limpio en negro puro (#000000).
        if (_storyboard.MaxEndTimeMs > 0 && timeMs > _storyboard.MaxEndTimeMs + 250.0)
        {
            _hasStoryboardFinished = true;
            ClearTextureCache();
            StopRenderTimer();
            return;
        }

        // Espacio de coordenadas de osu!:
        // El playfield base es 640x480 (con centro exacto en X = 320, Y = 240).
        // En pantallas 16:9 el lienzo virtual de osu! abarca 853.3333 x 480 (de X = -106.6667 a X = 746.6667),
        // manteniendo siempre (320, 240) en el centro geométrico de la pantalla y 480 unidades de alto.
        const float widescreenVirtualW = 853.3333f;
        const float virtualH = 480f;

        float scaleX = viewW / widescreenVirtualW;
        float scaleY = viewH / virtualH;
        float scale = _stretchMode == Stretch.UniformToFill
            ? Math.Max(scaleX, scaleY)
            : Math.Min(scaleX, scaleY);

        float offsetX = (viewW * 0.5f) - (320f * scale);
        float offsetY = (viewH * 0.5f) - (240f * scale);

        canvas.Save();

        // Recortar al rectángulo de cámara widescreen (-106.67 .. 746.67 x 0 .. 480)
        // para que los sprites aparcados fuera de escena no sean visibles
        var clipRect = SKRect.Create(offsetX + (-106.6667f * scale), offsetY, widescreenVirtualW * scale, virtualH * scale);
        canvas.ClipRect(clipRect);

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        var sprites = _storyboard.Sprites;
        int count = sprites.Count;
        for (int i = 0; i < count; i++)
        {
            var sprite = sprites[i];
            if (!sprite.HasCommands || timeMs < sprite.ActiveStartTime || timeMs > sprite.ActiveEndTime)
                continue;

            var state = sprite.Evaluate(timeMs);
            if (!state.IsVisible) continue;

            var bitmap = GetOrLoadTexture(_storyboard.FolderPath, state.TextureRelativePath);
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) continue;

            DrawSprite(canvas, bitmap, sprite.Origin, in state);
        }

        canvas.Restore();
    }

    private void DrawSprite(SKCanvas canvas, SKBitmap bitmap, StoryboardOrigin origin, in SpriteRenderState state)
    {
        var (pivotX, pivotY) = GetOriginPivot(origin, bitmap.Width, bitmap.Height);

        canvas.Save();
        canvas.Translate(state.X, state.Y);

        if (Math.Abs(state.RotationRad) > 0.0001f)
        {
            float degrees = state.RotationRad * (180f / MathF.PI);
            canvas.RotateDegrees(degrees);
        }

        canvas.Scale(state.ScaleX, state.ScaleY);

        byte alpha = (byte)Math.Clamp(state.Opacity * 255f, 0f, 255f);
        _spritePaint.BlendMode = state.Additive ? SKBlendMode.Plus : SKBlendMode.SrcOver;

        if (state.R == 255 && state.G == 255 && state.B == 255)
        {
            _spritePaint.ColorFilter = null;
            _spritePaint.Color = new SKColor(255, 255, 255, alpha);
        }
        else
        {
            uint colorKey = ((uint)alpha << 24) | ((uint)state.R << 16) | ((uint)state.G << 8) | state.B;
            if (!_colorFilterCache.TryGetValue(colorKey, out var filter))
            {
                if (_colorFilterCache.Count >= 512)
                {
                    foreach (var old in _colorFilterCache.Values)
                        old.Dispose();
                    _colorFilterCache.Clear();
                }
                filter = SKColorFilter.CreateBlendMode(new SKColor(state.R, state.G, state.B, alpha), SKBlendMode.Modulate);
                _colorFilterCache[colorKey] = filter;
            }
            _spritePaint.ColorFilter = filter;
            _spritePaint.Color = SKColors.White;
        }

        // En osu! (XNA SpriteBatch), FlipH / FlipV invierten los píxeles de la textura dentro de su propio
        // rectángulo [-pivotX .. width - pivotX], sin reflejar el rectángulo respecto al pivote cuando el origen no es Centre.
        if (state.FlipH || state.FlipV)
        {
            float halfW = bitmap.Width * 0.5f;
            float halfH = bitmap.Height * 0.5f;
            canvas.Translate(halfW - pivotX, halfH - pivotY);
            canvas.Scale(state.FlipH ? -1f : 1f, state.FlipV ? -1f : 1f);
            canvas.DrawBitmap(bitmap, -halfW, -halfH, _spritePaint);
        }
        else
        {
            canvas.DrawBitmap(bitmap, -pivotX, -pivotY, _spritePaint);
        }

        _spritePaint.ColorFilter = null;
        canvas.Restore();
    }

    private static (float X, float Y) GetOriginPivot(StoryboardOrigin origin, int width, int height)
    {
        float w = width;
        float h = height;
        return origin switch
        {
            StoryboardOrigin.TopLeft => (0f, 0f),
            StoryboardOrigin.TopCentre => (w * 0.5f, 0f),
            StoryboardOrigin.TopRight => (w, 0f),
            StoryboardOrigin.CentreLeft => (0f, h * 0.5f),
            StoryboardOrigin.Centre => (w * 0.5f, h * 0.5f),
            StoryboardOrigin.CentreRight => (w, h * 0.5f),
            StoryboardOrigin.BottomLeft => (0f, h),
            StoryboardOrigin.BottomCentre => (w * 0.5f, h),
            StoryboardOrigin.BottomRight => (w, h),
            _ => (w * 0.5f, h * 0.5f)
        };
    }

    private SKBitmap? GetOrLoadTexture(string folderPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        if (_textureCache.TryGetValue(relativePath, out var cached))
            return cached;

        // Límite preventivo de seguridad de caché por storyboard (máx 1200 texturas únicas en RAM)
        if (_textureCache.Count >= 1200)
        {
            _textureCache[relativePath] = null;
            return null;
        }

        SKBitmap? loaded = null;
        try
        {
            string fullPath = Path.Combine(folderPath, relativePath);
            if (!File.Exists(fullPath))
            {
                // En osu!, a veces el storyboard omite o cambia la extensión (.png vs .jpg)
                string withoutExt = Path.ChangeExtension(fullPath, null);
                if (File.Exists(withoutExt + ".png"))
                    fullPath = withoutExt + ".png";
                else if (File.Exists(withoutExt + ".jpg"))
                    fullPath = withoutExt + ".jpg";
                else if (File.Exists(withoutExt + ".jpeg"))
                    fullPath = withoutExt + ".jpeg";
            }

            if (File.Exists(fullPath))
            {
                loaded = SKBitmap.Decode(fullPath);
                loaded?.SetImmutable();
            }
        }
        catch
        {
            loaded = null;
        }

        _textureCache[relativePath] = loaded;
        return loaded;
    }
}
