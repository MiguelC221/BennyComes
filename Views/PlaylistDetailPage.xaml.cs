using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BenyComes.Models;
using BenyComes.Services.Audio;

namespace BenyComes.Views;

public partial class PlaylistDetailPage : UserControl
{
    public PlaylistDetailPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AudioPlayerService.Instance.TrackChanged -= OnTrackChanged;
        AudioPlayerService.Instance.TrackChanged += OnTrackChanged;

        if (AudioPlayerService.Instance.CurrentTrack is Track current)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollToTrackCentered(current)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AudioPlayerService.Instance.TrackChanged -= OnTrackChanged;
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        if (track == null) return;
        Dispatcher.BeginInvoke(new Action(() => ScrollToTrackCentered(track)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollToTrackCentered(Track track)
    {
        if (PlaylistTracksListView == null || PlaylistScrollViewer == null) return;

        int idx = -1;
        for (int i = 0; i < PlaylistTracksListView.Items.Count; i++)
        {
            if (PlaylistTracksListView.Items[i] is Track t && (t.Id == track.Id || t.FilePath == track.FilePath))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) return;

        PlaylistTracksListView.UpdateLayout();
        if (PlaylistTracksListView.ItemContainerGenerator.ContainerFromIndex(idx) is FrameworkElement item)
        {
            var transform = item.TransformToAncestor(PlaylistScrollViewer);
            Point pt = transform.Transform(new Point(0, 0));
            double itemAbsoluteTop = PlaylistScrollViewer.VerticalOffset + pt.Y;
            double itemCenter = itemAbsoluteTop + (item.ActualHeight / 2.0);
            double viewportHalf = PlaylistScrollViewer.ViewportHeight / 2.0;

            // Si está de primero esperar hasta que llegue a la mitad, y si no se puede bajar parar
            double targetOffset = itemCenter - viewportHalf;
            double maxOffset = Math.Max(0, PlaylistScrollViewer.ScrollableHeight);
            targetOffset = Math.Clamp(targetOffset, 0, maxOffset);

            PlaylistScrollViewer.ScrollToVerticalOffset(targetOffset);
        }
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