using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BenyComes.Services;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnAccentEmeraldClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(0, 210, 106));
    }

    private void OnAccentBlueClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(0, 120, 215));
    }

    private void OnAccentCyanClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(0, 183, 195));
    }

    private void OnAccentPurpleClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(135, 100, 184));
    }

    private void OnAccentMagentaClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(227, 0, 140));
    }

    private void OnAccentOrangeClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.SetAccentColor(Color.FromRgb(255, 140, 0));
    }
}
