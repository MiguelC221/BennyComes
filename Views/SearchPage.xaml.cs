using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BenyComes.Models;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class SearchPage : UserControl
{
    public SearchPage()
    {
        InitializeComponent();
    }

    private void OnTrackItemDoubleClick(object sender, MouseButtonEventArgs e)
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
            vm.Search.PlayTrackCommand.Execute(track);
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

    private void OnTracksListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled)
        {
            e.Handled = true;
            SearchScrollViewer.ScrollToVerticalOffset(SearchScrollViewer.VerticalOffset - (e.Delta * 0.75));
        }
    }

    private void OnTrackItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView lv && lv.SelectedItem != null)
        {
            if (lv.ItemContainerGenerator.ContainerFromItem(lv.SelectedItem) is FrameworkElement item)
            {
                item.BringIntoView();
            }
        }
    }

    private void OnPageSearchBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Search.ActiveSearchSource = "Page";
        }
    }

    private void OnPageSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.Search.ActiveSearchSource = "Page";

        if (e.Key == Key.Down)
        {
            if (vm.Search.IsPageSuggestionsOpen)
            {
                e.Handled = true;
                vm.Search.MoveSuggestionSelectionCommand.Execute(1);
            }
        }
        else if (e.Key == Key.Up)
        {
            if (vm.Search.IsPageSuggestionsOpen)
            {
                e.Handled = true;
                vm.Search.MoveSuggestionSelectionCommand.Execute(-1);
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (vm.Search.IsPageSuggestionsOpen && vm.Search.SelectedSuggestionIndex >= 0)
            {
                vm.Search.SelectCurrentHighlightedSuggestionCommand.Execute(null);
            }
            else
            {
                _ = vm.Search.ExecuteFullSearchAsync();
            }

            if (sender is UIElement uie)
            {
                uie.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.Search.CloseSuggestionsCommand.Execute(null);
        }
    }
}
