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
        if (sender is ListView lv && lv.SelectedItem is Track track && DataContext is MainViewModel vm)
        {
            vm.Search.PlayTrackCommand.Execute(track);
        }
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
}
