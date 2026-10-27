using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Appearance;

namespace BenyComes.Services;

public partial class ThemeService : ObservableObject
{
    private static readonly Lazy<ThemeService> _lazy = new(() => new ThemeService());
    public static ThemeService Instance => _lazy.Value;

    public static readonly List<(string Name, Color Color)> PresetAccents = new()
    {
        ("Verde Esmeralda", Color.FromRgb(0, 210, 106)),
        ("Azul Groove", Color.FromRgb(0, 120, 215)),
        ("Cian Hi-Fi", Color.FromRgb(0, 183, 195)),
        ("Morado Eléctrico", Color.FromRgb(135, 100, 184)),
        ("Rosa Magenta", Color.FromRgb(227, 0, 140)),
        ("Naranja Neón", Color.FromRgb(255, 140, 0))
    };

    [ObservableProperty]
    private Color _currentAccentColor = Color.FromRgb(0, 210, 106);

    [ObservableProperty]
    private SolidColorBrush _currentAccentBrush = new(Color.FromRgb(0, 210, 106));

    [ObservableProperty]
    private bool _autoExtractAccent = true;

    [ObservableProperty]
    private string _currentTheme = "Oscuro";

    [ObservableProperty]
    private Brush _miniPlayerBackgroundBrush = new SolidColorBrush(Color.FromArgb(245, 20, 22, 30));

    [ObservableProperty]
    private SolidColorBrush _accentForegroundBrush = new(Colors.White);

    private Color _lastRawExtractedColor = Color.FromRgb(0, 210, 106);

    private ThemeService()
    {
        SetTheme(_currentTheme);
        ApplyAccentColor(_currentAccentColor);
    }

    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        bool isLight = theme == "Claro";

        if (Application.Current != null)
        {
            Action applyTheme = () =>
            {
                try
                {
                    ApplicationThemeManager.Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
                }
                catch { }

                if (isLight)
                {
                    Application.Current.Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));
                    Application.Current.Resources["SurfaceCardBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["CardSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["SurfaceCardBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE0, 0xE8));
                    Application.Current.Resources["SurfaceCardHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xEC, 0xEF));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x18));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5C));
                    Application.Current.Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0x76, 0x7B, 0x87));
                    Application.Current.Resources["NavPaneBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF6));
                    Application.Current.Resources["DividerBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE0, 0xE8));
                    Application.Current.Resources["ScrollBarThumbBrush"] = new SolidColorBrush(Color.FromArgb(0x77, 0x50, 0x55, 0x65));
                    Application.Current.Resources["ScrollBarThumbHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xAA, 0x30, 0x35, 0x45));
                    Application.Current.Resources["ScrollBarThumbDraggingBrush"] = new SolidColorBrush(Color.FromArgb(0xDD, 0x15, 0x18, 0x22));
                }
                else
                {
                    Application.Current.Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x12));
                    Application.Current.Resources["SurfaceCardBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
                    Application.Current.Resources["CardSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
                    Application.Current.Resources["SurfaceCardBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["SurfaceCardHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34));
                    Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["TextTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["NavPaneBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C));
                    Application.Current.Resources["DividerBrush"] = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["ScrollBarThumbBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["ScrollBarThumbHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                    Application.Current.Resources["ScrollBarThumbDraggingBrush"] = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                }
            };

            if (Application.Current.Dispatcher.CheckAccess())
                applyTheme();
            else
                Application.Current.Dispatcher.Invoke(applyTheme);

            if (AutoExtractAccent)
            {
                ApplyAccentColor(EnsureVibrantAccent(_lastRawExtractedColor, !isLight));
            }
        }
    }

    public void SetAccentColor(Color color)
    {
        AutoExtractAccent = false;
        ApplyAccentColor(color);
    }

    public static Color GetDominantTintedColor(BitmapSource? image)
    {
        var dominant = MetadataService.ExtractDominantAccentColor(image);
        double r = dominant.R / 255.0;
        double g = dominant.G / 255.0;
        double b = dominant.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double h = 0, s = 0;

        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else if (max == b) h = (r - g) / d + 4;
            h /= 6.0;
        }

        // Clamp L between 20% and 30% for high contrast with white text
        l = Math.Clamp(l, 0.20, 0.32);
        s = Math.Clamp(s * 1.25, 0.35, 0.85);

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        double q2 = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p2 = 2.0 * l - q2;
        byte finalR = (byte)Math.Round(HueToRgb(p2, q2, h + 1.0 / 3.0) * 255);
        byte finalG = (byte)Math.Round(HueToRgb(p2, q2, h) * 255);
        byte finalB = (byte)Math.Round(HueToRgb(p2, q2, h - 1.0 / 3.0) * 255);

        return Color.FromArgb(245, finalR, finalG, finalB);
    }

    public void OnTrackArtChanged(BitmapSource? art)
    {
        if (art != null)
        {
            var dominantColor = MetadataService.ExtractDominantAccentColor(art);
            
            // Color de fondo sólido que reacciona a la carátula:
            // Tomamos el color dominante y lo adaptamos como fondo sólido tintado elegante y oscuro
            double r = dominantColor.R / 255.0;
            double g = dominantColor.G / 255.0;
            double b = dominantColor.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double h = 0, s = 0;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else if (max == b) h = (r - g) / d + 4;
                h /= 6.0;
            }

            // Luminosidad ajustada (~32%) y saturación equilibrada para un fondo visible, vivo y que reacciona con claridad al álbum
            l = 0.32;
            s = Math.Clamp(s, 0.28, 0.50);

            static double HueToRgb(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0 / 2.0) return q;
                if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
                return p;
            }

            double q2 = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p2 = 2.0 * l - q2;
            byte finalR = (byte)Math.Round(HueToRgb(p2, q2, h + 1.0 / 3.0) * 255);
            byte finalG = (byte)Math.Round(HueToRgb(p2, q2, h) * 255);
            byte finalB = (byte)Math.Round(HueToRgb(p2, q2, h - 1.0 / 3.0) * 255);

            var solidColor = Color.FromRgb(finalR, finalG, finalB);
            var solidBrush = new SolidColorBrush(solidColor);
            solidBrush.Freeze();
            MiniPlayerBackgroundBrush = solidBrush;

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Application.Current.Resources["MiniPlayerBackgroundBrush"] = solidBrush;
                });
            }

            if (AutoExtractAccent)
            {
                _lastRawExtractedColor = dominantColor;
                var adjusted = EnsureVibrantAccent(dominantColor, CurrentTheme != "Claro");
                ApplyAccentColor(adjusted);
            }
        }
        else
        {
            var fallbackBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x1D, 0x24));
            fallbackBrush.Freeze();
            MiniPlayerBackgroundBrush = fallbackBrush;

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Application.Current.Resources["MiniPlayerBackgroundBrush"] = fallbackBrush;
                });
            }
        }
    }

    public static Color EnsureVibrantAccent(Color rawColor, bool isDarkTheme)
    {
        double r = rawColor.R / 255.0;
        double g = rawColor.G / 255.0;
        double b = rawColor.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double delta = max - min;
        double s = 0.0;
        double h = 0.0;

        if (delta > 0.0001)
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (Math.Abs(max - r) < 0.0001)
                h = (g - b) / delta + (g < b ? 6.0 : 0.0);
            else if (Math.Abs(max - g) < 0.0001)
                h = (b - r) / delta + 2.0;
            else
                h = (r - g) / delta + 4.0;
            h /= 6.0;
        }

        // If the cover is black/gray/achromatic (saturation < 0.12 or max < 0.12)
        if (s < 0.12 || max < 0.12)
        {
            // Sky blue #60A5FA by default
            return Color.FromRgb(0x60, 0xA5, 0xFA);
        }

        // In Dark Mode: force minimum Luminosity of 62% (L = Math.Max(L, 0.62f)) and minimum Saturation of 55%
        if (isDarkTheme)
        {
            l = Math.Max(l, 0.62);
            s = Math.Max(s, 0.55);
        }
        else
        {
            // In Light Mode: clamp luminosity so it remains visible against light backgrounds
            l = Math.Clamp(l, 0.35, 0.48);
            s = Math.Max(s, 0.60);
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        double q2 = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        double p2 = 2.0 * l - q2;
        byte finalR = (byte)Math.Clamp((int)Math.Round(HueToRgb(p2, q2, h + 1.0 / 3.0) * 255.0), 0, 255);
        byte finalG = (byte)Math.Clamp((int)Math.Round(HueToRgb(p2, q2, h) * 255.0), 0, 255);
        byte finalB = (byte)Math.Clamp((int)Math.Round(HueToRgb(p2, q2, h - 1.0 / 3.0) * 255.0), 0, 255);

        return Color.FromRgb(finalR, finalG, finalB);
    }

    public void ApplyAccentColor(Color color)
    {
        CurrentAccentColor = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        CurrentAccentBrush = brush;

        // Calculate background perceived brightness / luminance
        double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        Color fgColor = luminance < 140 ? Colors.White : Color.FromRgb(0x11, 0x13, 0x18);
        var fgBrush = new SolidColorBrush(fgColor);
        fgBrush.Freeze();
        AccentForegroundBrush = fgBrush;

        if (Application.Current != null)
        {
            Action updateResources = () =>
            {
                Application.Current.Resources["SystemAccentBrush"] = brush;
                Application.Current.Resources["SystemAccentColor"] = color;
                Application.Current.Resources["AccentBrush"] = brush;
                Application.Current.Resources["AccentForegroundBrush"] = fgBrush;
            };

            if (Application.Current.Dispatcher.CheckAccess())
                updateResources();
            else
                Application.Current.Dispatcher.InvokeAsync(updateResources);
        }
    }
}
