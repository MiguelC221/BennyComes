using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BenyComes.Models;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class MyMusicPage : UserControl
{
    public MyMusicPage()
    {
        InitializeComponent();
    }

    private void OnSongsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 0;
        }
    }

    private void OnArtistsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 1;
        }
    }

    private void OnAlbumsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 2;
        }
    }

    private void OnSongsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 0;
        }
    }

    private void OnArtistsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 1;
        }
    }

    private void OnAlbumsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.SelectedTabIndex = 2;
        }
    }

    private void OnSongItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView lv && lv.SelectedItem is Track track && DataContext is MainViewModel vm)
        {
            vm.MyMusic.PlayTrackCommand.Execute(track);
        }
    }

    private void OnAddMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnArtistsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset > 0 && e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 150)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.MyMusic.LoadMoreArtists();
            }
        }
    }

    private void OnAlbumsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset > 0 && e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 150)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.MyMusic.LoadMoreAlbums();
            }
        }
    }
}
