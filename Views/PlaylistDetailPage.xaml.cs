using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BenyComes.Views;

public partial class PlaylistDetailPage : UserControl
{
    public PlaylistDetailPage()
    {
        InitializeComponent();
    }

    private void OnListViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            PlaylistScrollViewer?.RaiseEvent(eventArg);
        }
    }
}
