using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BenyComes.Views;

public partial class RecentPlaysPage : UserControl
{
    public RecentPlaysPage()
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
            MainScrollViewer?.RaiseEvent(eventArg);
        }
    }
}
