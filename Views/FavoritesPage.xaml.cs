using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class FavoritesPage : UserControl
{
    public FavoritesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Favorites.RequestScrollToLetter -= OnRequestScrollToLetter;
            vm.Favorites.RequestScrollToLetter += OnRequestScrollToLetter;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Favorites.RequestScrollToLetter -= OnRequestScrollToLetter;
        }
    }

    private void OnAlphaHeaderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Favorites.OpenJumpListCommand.Execute(null);
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

    private ListView GetActiveListView(MainViewModel vm)
    {
        if (vm.Favorites.IsCombinedMode)
            return CombinedSongsListView;
        return vm.Favorites.SelectedTabIndex == 0 ? LibrarySongsListView : OsuSongsListView;
    }

    private void OnRequestScrollToLetter(string letter)
    {
        if (DataContext is not MainViewModel vm) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var activeCollection = vm.Favorites.GetActiveSongsCollection();
                var targetListView = GetActiveListView(vm);
                var track = activeCollection.FirstOrDefault(s => string.Equals(s.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                if (track != null && targetListView != null)
                {
                    var group = targetListView.ItemContainerGenerator.Items.OfType<CollectionViewGroup>()
                        .FirstOrDefault(g => string.Equals(g.Name?.ToString(), letter, StringComparison.OrdinalIgnoreCase));

                    targetListView.ScrollIntoView(track);

                    if (group != null && targetListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement groupEl)
                    {
                        ScrollItemToTop(targetListView, groupEl);
                        return;
                    }

                    if (targetListView.ItemContainerGenerator.ContainerFromItem(track) is FrameworkElement trackEl)
                    {
                        ScrollItemToTop(targetListView, trackEl);
                        return;
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (group != null && targetListView.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement gEl2)
                        {
                            ScrollItemToTop(targetListView, gEl2);
                        }
                        else if (targetListView.ItemContainerGenerator.ContainerFromItem(track) is FrameworkElement tEl2)
                        {
                            ScrollItemToTop(targetListView, tEl2);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
        }));
    }
}
