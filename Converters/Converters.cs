using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BenyComes.Models;
using Wpf.Ui.Controls;

namespace BenyComes.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = false;
        if (value is bool val)
            b = val;
        else if (value is int count)
            b = count > 0;
        else if (value is long lCount)
            b = lCount > 0;
        else if (value != null)
            b = true;

        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PlayPauseSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isPlaying = value is bool b && b;
        return isPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class RepeatModeToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.One => SymbolRegular.ArrowRepeat124,
                RepeatMode.All => SymbolRegular.ArrowRepeatAll24,
                _ => SymbolRegular.ArrowRepeatAllOff24
            };
        }
        return SymbolRegular.ArrowRepeatAllOff24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class VolumeToSymbolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is float vol && values[1] is bool isMuted)
        {
            if (isMuted || vol <= 0.01f)
                return SymbolRegular.SpeakerMute24;
            if (vol < 0.4f)
                return SymbolRegular.Speaker124;
            return SymbolRegular.Speaker224;
        }
        return SymbolRegular.Speaker224;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (Invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringToVisibilityConverter : NullToVisibilityConverter
{
}

