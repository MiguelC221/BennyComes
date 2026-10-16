using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class HomePage : UserControl
{
    public HomePage()
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
            HomeScrollViewer?.RaiseEvent(eventArg);
        }
    }

    private void OnSmartMixContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && DataContext is MainViewModel vm)
        {
            int cols = Math.Max(2, (int)(e.NewSize.Width / 250));
            vm.Home.SmartMixColumns = cols;
        }
    }
}
