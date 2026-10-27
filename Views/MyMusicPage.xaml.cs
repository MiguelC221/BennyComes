using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BenyComes.Models;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class MyMusicPage : UserControl
{
    public MyMusicPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.RequestScrollToLetter -= OnRequestScrollToLetter;
            vm.MyMusic.RequestScrollToLetter += OnRequestScrollToLetter;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.RequestScrollToLetter -= OnRequestScrollToLetter;
        }
    }

    private void OnAlphaHeaderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MyMusic.OpenJumpListCommand.Execute(null);
        }
    }

    private void OnRequestScrollToLetter(string letter)
    {
        if (DataContext is not MainViewModel vm) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (vm.MyMusic.SelectedTabIndex == 0) // Canciones
                {
                    var group = SongsListView.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                        .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                    if (group != null && SongsListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupElement)
                    {
                        groupElement.BringIntoView();
                        return;
                    }

                    var track = vm.MyMusic.Songs.FirstOrDefault(s => string.Equals(s.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (track != null)
                    {
                        SongsListView.ScrollIntoView(track);
                    }
                }
                else if (vm.MyMusic.SelectedTabIndex == 1) // Artistas
                {
                    var group = ArtistsItemsControl.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                        .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                    if (group != null && ArtistsItemsControl.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupElement)
                    {
                        groupElement.BringIntoView();
                        return;
                    }

                    var artist = vm.MyMusic.Artists.FirstOrDefault(a => string.Equals(a.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (artist != null && ArtistsItemsControl.ItemContainerGenerator.ContainerFromItem(artist) is FrameworkElement artistElement)
                    {
                        artistElement.BringIntoView();
                    }
                }
                else if (vm.MyMusic.SelectedTabIndex == 2) // Álbumes
                {
                    var group = AlbumsItemsControl.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                        .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                    if (group != null && AlbumsItemsControl.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupElement)
                    {
                        groupElement.BringIntoView();
                        return;
                    }

                    var album = vm.MyMusic.Albums.FirstOrDefault(a => string.Equals(a.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (album != null && AlbumsItemsControl.ItemContainerGenerator.ContainerFromItem(album) is FrameworkElement albumElement)
                    {
                        albumElement.BringIntoView();
                    }
                }
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        if (track != null && DataContext is MainViewModel vm)
        {
            vm.MyMusic.PlayTrackCommand.Execute(track);
        }
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

    private long _lastArtistHeaderToggleTick;

    private void OnArtistDetailPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.MyMusic.SelectedArtistDetail == null)
            return;

        if (e.Delta < 0)
        {
            // El simple hecho de bajar con la rueda del ratón comprime la información del artista con animación
            if (!vm.MyMusic.IsArtistHeaderCompact)
            {
                _lastArtistHeaderToggleTick = Environment.TickCount64;
                vm.MyMusic.IsArtistHeaderCompact = true;
            }
        }
        else if (e.Delta > 0)
        {
            var activeScroll = vm.MyMusic.IsArtistAlbumsGridView ? ArtistDetailGridScrollViewer : ArtistDetailListScrollViewer;
            double offset = activeScroll?.VerticalOffset ?? 0.0;
            if (offset <= 25.0 && vm.MyMusic.IsArtistHeaderCompact)
            {
                _lastArtistHeaderToggleTick = Environment.TickCount64;
                vm.MyMusic.IsArtistHeaderCompact = false;
            }
        }
    }

    private void OnArtistDetailScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.MyMusic.SelectedArtistDetail == null)
            return;

        // Ignorar eventos de reajuste de altura del Viewport provocados por la propia animación del encabezado
        if (Math.Abs(e.ViewportHeightChange) > 0.5 && Math.Abs(e.VerticalChange) < 0.5)
            return;

        // Cooldown de 320ms durante la animación de compresión/expansión para evitar oscilaciones visuales
        if (Environment.TickCount64 - _lastArtistHeaderToggleTick < 320)
            return;

        if (!vm.MyMusic.IsArtistHeaderCompact && e.VerticalChange > 0 && e.VerticalOffset > 45.0)
        {
            _lastArtistHeaderToggleTick = Environment.TickCount64;
            vm.MyMusic.IsArtistHeaderCompact = true;
        }
        else if (vm.MyMusic.IsArtistHeaderCompact && e.VerticalOffset <= 5.0)
        {
            _lastArtistHeaderToggleTick = Environment.TickCount64;
            vm.MyMusic.IsArtistHeaderCompact = false;
        }
    }

    private void OnInnerListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled && sender is UIElement element)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement;
            parent?.RaiseEvent(eventArg);
        }
    }
}
