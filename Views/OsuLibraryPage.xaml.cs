using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
            vm.OsuLibrary.PropertyChanged -= OnOsuLibraryPropertyChanged;
            vm.OsuLibrary.PropertyChanged += OnOsuLibraryPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.RequestScrollToLetter -= OnRequestScrollToLetter;
            vm.OsuLibrary.PropertyChanged -= OnOsuLibraryPropertyChanged;
        }
    }

    private void OnOsuLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.PropertyName == nameof(OsuLibraryViewModel.SelectedTabIndex) ||
            (e.PropertyName == nameof(OsuLibraryViewModel.IsInDetailView) && !vm.OsuLibrary.IsInDetailView))
        {
            PlayTabSwitchAnimation(vm.OsuLibrary.SelectedTabIndex);
        }
    }

    private void PlayTabSwitchAnimation(int tabIndex)
    {
        Grid? targetGrid = tabIndex switch
        {
            0 => Tab0SongsGrid,
            1 => Tab1ArtistsGrid,
            2 => Tab2VideoSongsGrid,
            _ => null
        };

        TranslateTransform? targetTranslate = tabIndex switch
        {
            0 => Tab0SongsTranslate,
            1 => Tab1ArtistsTranslate,
            2 => Tab2VideoSongsTranslate,
            _ => null
        };

        if (targetGrid == null || targetTranslate == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = ease
        };
        var slideAnim = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = ease
        };

        targetGrid.BeginAnimation(OpacityProperty, fadeAnim);
        targetTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void OnAlphaHeaderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.OpenJumpListCommand.Execute(null);
        }
    }

    private void OnSongsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 0;
        }
    }

    private void OnSongsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 0;
        }
    }

    private void OnArtistsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 1;
        }
    }

    private void OnArtistsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 1;
        }
    }

    private void OnVideoSongsTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 2;
        }
    }

    private void OnVideoSongsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OsuLibrary.SelectedTabIndex = 2;
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
            double scrollDelta = (e.Delta / 120.0) * 48.0;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollDelta);
            e.Handled = true;
        }
    }

    private void OnVideoSongsListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = GetScrollViewer(VideoSongsListView);
        if (sv != null)
        {
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
            double scrollDelta = (e.Delta / 120.0) * 48.0;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollDelta);
            e.Handled = true;
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
            vm.OsuLibrary.PlayTrackCommand.Execute(track);
        }
    }

    private void OnRequestScrollToLetter(string letter)
    {
        if (DataContext is not MainViewModel vm) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (vm.OsuLibrary.SelectedTabIndex == 0 || vm.OsuLibrary.SelectedTabIndex == 2) // Canciones o Con vídeo
                {
                    var targetListView = vm.OsuLibrary.SelectedTabIndex == 2 ? VideoSongsListView : SongsListView;
                    var sourceTracks = vm.OsuLibrary.SelectedTabIndex == 2 ? vm.OsuLibrary.VideoSongs : vm.OsuLibrary.Songs;
                    var track = sourceTracks.FirstOrDefault(s => string.Equals(s.AlphaGroup, letter, StringComparison.OrdinalIgnoreCase));
                    if (track != null)
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
                else if (vm.OsuLibrary.SelectedTabIndex == 1) // Artistas
                {
                    var section = vm.OsuLibrary.ArtistSections.FirstOrDefault(s => string.Equals(s.Letter, letter, StringComparison.OrdinalIgnoreCase));
                    if (section != null)
                    {
                        ArtistsListView.ScrollIntoView(section);
                        ArtistsListView.UpdateLayout();

                        if (ArtistsListView.ItemContainerGenerator.ContainerFromItem(section) is FrameworkElement sectionEl)
                        {
                            ScrollItemToTop(ArtistsListView, sectionEl);
                            return;
                        }

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (ArtistsListView.ItemContainerGenerator.ContainerFromItem(section) is FrameworkElement secEl2)
                            {
                                ScrollItemToTop(ArtistsListView, secEl2);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch { }
        }));
    }
}
