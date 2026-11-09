using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
/// Lienzo 2D acelerado por SkiaSharp para renderizar Storyboards de osu! (Nivel 1: Básico / Ambiental).
/// Proyecta el espacio virtual de osu! (640x480 / 853.33x480 widescreen) y administra la caché de texturas por beatmap.
/// </summary>
public sealed class OsuStoryboardControl : SKElement
{
    private OsuStoryboard? _storyboard;
    private readonly Dictionary<string, SKBitmap?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
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
            if (_isPlaying)
                _clockInterpolator.Restart();
            else
                _clockInterpolator.Reset();

            UpdateTimerState();
            InvalidateVisual();
        }
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
        foreach (var bmp in _textureCache.Values)
        {
            bmp?.Dispose();
        }
        _textureCache.Clear();
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
        // El playfield base es 640x480 (con centro en 320, 240).
        // En Widescreen (16:9), el área visible se extiende de X = -106.67 a X = 746.67 (853.33 x 480), manteniendo el centro en (320, 240).
        float virtualW = _storyboard.WidescreenStoryboard ? 853.3333f : 640f;
        float virtualH = 480f;

        float scaleX = viewW / virtualW;
        float scaleY = viewH / virtualH;
        float scale = _stretchMode == Stretch.UniformToFill
            ? Math.Max(scaleX, scaleY)
            : Math.Min(scaleX, scaleY);

        float offsetX = (viewW * 0.5f) - (320f * scale);
        float offsetY = (viewH * 0.5f) - (240f * scale);

        canvas.Save();

        // Si está en modo Uniform (con bandas laterales/superiores), recortar al rectángulo virtual del storyboard
        if (_stretchMode == Stretch.Uniform)
        {
            float leftEdge = _storyboard.WidescreenStoryboard ? -106.6667f : 0f;
            var clipRect = SKRect.Create(offsetX + leftEdge * scale, offsetY, virtualW * scale, virtualH * scale);
            canvas.ClipRect(clipRect);
        }

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        var sprites = _storyboard.Sprites;
        for (int i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
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

        float sx = state.FlipH ? -state.ScaleX : state.ScaleX;
        float sy = state.FlipV ? -state.ScaleY : state.ScaleY;
        canvas.Scale(sx, sy);

        byte alpha = (byte)Math.Clamp(state.Opacity * 255f, 0f, 255f);
        _spritePaint.BlendMode = state.Additive ? SKBlendMode.Plus : SKBlendMode.SrcOver;

        if (state.R == 255 && state.G == 255 && state.B == 255)
        {
            _spritePaint.ColorFilter = null;
            _spritePaint.Color = new SKColor(255, 255, 255, alpha);
            canvas.DrawBitmap(bitmap, -pivotX, -pivotY, _spritePaint);
        }
        else
        {
            var tintColor = new SKColor(state.R, state.G, state.B, alpha);
            using var filter = SKColorFilter.CreateBlendMode(tintColor, SKBlendMode.Modulate);
            _spritePaint.ColorFilter = filter;
            _spritePaint.Color = SKColors.White;
            canvas.DrawBitmap(bitmap, -pivotX, -pivotY, _spritePaint);
            _spritePaint.ColorFilter = null;
        }

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
