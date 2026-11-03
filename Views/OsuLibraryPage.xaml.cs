using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BenyComes.Models;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class OsuLibraryPage : UserControl
{
    public OsuLibraryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.RequestScrollToLetter -= OnRequestScrollToLetter;
            vm.OsuLibrary.RequestScrollToLetter += OnRequestScrollToLetter;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.RequestScrollToLetter -= OnRequestScrollToLetter;
        }
    }

    private void OnAlphaHeaderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.OpenJumpListCommand.Execute(null);
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
                if (vm.OsuLibrary.SelectedTabIndex == 0) // Canciones
                {
                    var track = vm.OsuLibrary.Songs.FirstOrDefault(s => string.Equals(s.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (track != null)
                    {
                        var group = SongsListView.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                            .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                        SongsListView.ScrollIntoView(track);

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
                else if (vm.OsuLibrary.SelectedTabIndex == 1) // Artistas
                {
                    var artist = vm.OsuLibrary.Artists.FirstOrDefault(a => string.Equals(a.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
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
                            return;
                        }

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (group != null && ArtistsListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement gEl2)
                            {
                                ScrollItemToTop(ArtistsListView, gEl2);
                            }
                            else if (ArtistsListView.ItemContainerGenerator.ContainerFromItem(artist) is FrameworkElement aEl2)
                            {
                                ScrollItemToTop(ArtistsListView, aEl2);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch { }
        }));
    }
}
