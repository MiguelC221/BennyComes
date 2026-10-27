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

    private static ScrollViewer? GetScrollViewer(DependencyObject dep)
    {
        if (dep is ScrollViewer sv) return sv;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void ScrollItemToTop(ListView listView, FrameworkElement element)
    {
        var sv = GetScrollViewer(listView);
        if (sv != null)
        {
            var transform = element.TransformToVisual(sv);
            var offset = transform.Transform(new Point(0, 0));
            sv.ScrollToVerticalOffset(sv.VerticalOffset + offset.Y);
        }
        else
        {
            element.BringIntoView();
        }
    }

    private void OnSongsListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = GetScrollViewer(SongsListView);
        if (sv != null)
        {
            // Movimiento suave y controlado: 48px por muesca estándar (120 de delta)
            double scrollDelta = (e.Delta / 120.0) * 48.0;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollDelta);
            e.Handled = true;
        }
    }

    private void OnArtistsListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = GetScrollViewer(ArtistsListView);
        if (sv != null)
        {
            // Movimiento suave y controlado en artistas (48px por muesca estándar)
            double scrollDelta = (e.Delta / 120.0) * 48.0;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollDelta);
            e.Handled = true;
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
                    var track = vm.MyMusic.Songs.FirstOrDefault(s => string.Equals(s.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (track != null)
                    {
                        var group = SongsListView.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                            .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                        // 1. Desplazar primero al final/inicio para materializar contenedores virtuales
                        SongsListView.ScrollIntoView(track);

                        // 2. Dar prioridad al encabezado del grupo para que quede en el top exacto
                        if (group != null && SongsListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupEl)
                        {
                            ScrollItemToTop(SongsListView, groupEl);
                            return;
                        }

                        if (SongsListView.ItemContainerGenerator.ContainerFromItem(track) is FrameworkElement trackEl)
                        {
                            ScrollItemToTop(SongsListView, trackEl);
                            return;
                        }

                        // Si los contenedores aún se están generando, reintentar al siguiente ciclo
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (group != null && SongsListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement gEl2)
                            {
                                ScrollItemToTop(SongsListView, gEl2);
                            }
                            else if (SongsListView.ItemContainerGenerator.ContainerFromItem(track) is FrameworkElement tEl2)
                            {
                                ScrollItemToTop(SongsListView, tEl2);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                else if (vm.MyMusic.SelectedTabIndex == 1) // Artistas
                {
                    var artist = vm.MyMusic.Artists.FirstOrDefault(a => string.Equals(a.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (artist != null)
                    {
                        var group = ArtistsListView.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                            .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                        ArtistsListView.ScrollIntoView(artist);

                        if (group != null && ArtistsListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupEl)
                        {
                            ScrollItemToTop(ArtistsListView, groupEl);
                            return;
                        }

                        if (ArtistsListView.ItemContainerGenerator.ContainerFromItem(artist) is FrameworkElement artistEl)
                        {
                            ScrollItemToTop(ArtistsListView, artistEl);
                        }
                    }
                }
                else if (vm.MyMusic.SelectedTabIndex == 2) // Álbumes
                {
                    var album = vm.MyMusic.Albums.FirstOrDefault(a => string.Equals(a.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (album != null)
                    {
                        AlbumsListView.ScrollIntoView(album);
                        if (AlbumsListView.ItemContainerGenerator.ContainerFromItem(album) is FrameworkElement albumElement)
                        {
                            ScrollItemToTop(AlbumsListView, albumElement);
                        }
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
