using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class NowPlayingView : UserControl
{
    private bool _isPrimaryActive = true;

    public NowPlayingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.NowPlaying != null)
        {
            vm.NowPlaying.PropertyChanged -= OnNowPlayingPropertyChanged;
            vm.NowPlaying.PropertyChanged += OnNowPlayingPropertyChanged;
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            if (vm.NowPlaying.BackgroundImage != null)
            {
                BackgroundPrimary.Source = vm.NowPlaying.BackgroundImage;
            }
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsNowPlayingQueueExpanded) &&
            sender is MainViewModel vm && vm.IsNowPlayingQueueExpanded)
        {
            // Esperar a que el layout de la cola exista antes de desplazar
            Dispatcher.BeginInvoke(new Action(() => ScrollToCurrentTrack(center: true)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnLocateCurrentTrackClick(object sender, RoutedEventArgs e)
    {
        ScrollToCurrentTrack(center: true);
    }

    private void ScrollToCurrentTrack(bool center)
    {
        var current = Services.Audio.AudioPlayerService.Instance.CurrentTrack;
        if (current == null || QueueListView.Items.Count == 0) return;

        int index = QueueListView.Items.IndexOf(current);
        if (index < 0) return;

        QueueListView.ScrollIntoView(current);
        if (!center) return;

        QueueListView.UpdateLayout();
        var sv = FindScrollViewer(QueueListView);
        if (sv == null) return;

        if (sv.CanContentScroll)
        {
            // Unidades lógicas = índice de elemento
            sv.ScrollToVerticalOffset(Math.Max(0, index - sv.ViewportHeight / 2.0));
        }
        else if (QueueListView.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
        {
            var pt = item.TranslatePoint(new Point(0, 0), sv);
            sv.ScrollToVerticalOffset(sv.VerticalOffset + pt.Y - (sv.ViewportHeight - item.ActualHeight) / 2.0);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer s) return s;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundImage) && DataContext is MainViewModel vm)
        {
            var newImage = vm.NowPlaying.BackgroundImage;
            if (newImage != null)
            {
                CrossfadeBackground(newImage);
            }
        }
    }

    private void CrossfadeBackground(BitmapImage newImage)
    {
        var duration = TimeSpan.FromMilliseconds(500);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (_isPrimaryActive)
        {
            BackgroundSecondary.Source = newImage;
            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = false;
        }
        else
        {
            BackgroundPrimary.Source = newImage;
            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = true;
        }
    }

    private bool _isDraggingSeek;
    private bool _isDraggingVolume;

    private static void SetSliderValueFromMousePosition(Slider slider, System.Windows.Input.MouseEventArgs e)
    {
        if (slider.ActualWidth <= 0) return;
        Point pt = e.GetPosition(slider);
        double ratio = Math.Clamp(pt.X / slider.ActualWidth, 0.0, 1.0);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
    }

    private void OnSeekContainerMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NowPlayingSeekSlider != null && DataContext is MainViewModel vm)
        {
            _isDraggingSeek = true;
            vm.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
        }
    }

    private void OnSeekContainerMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && NowPlayingSeekSlider != null)
        {
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
        }
    }

    private void OnSeekContainerMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingSeek && NowPlayingSeekSlider != null && DataContext is MainViewModel vm)
        {
            SetSliderValueFromMousePosition(NowPlayingSeekSlider, e);
            _isDraggingSeek = false;
            vm.NowPlaying.SeekCommand.Execute(NowPlayingSeekSlider.Value);
            vm.NowPlaying.IsUserSeeking = false;
        }
    }

    private void OnSeekSliderPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            _isDraggingSeek = true;
            vm.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider && DataContext is MainViewModel vm)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingSeek = false;
            vm.NowPlaying.SeekCommand.Execute(slider.Value);
            vm.NowPlaying.IsUserSeeking = false;
        }
    }

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is MainViewModel vm && sender is Slider slider && (slider.IsMouseCaptureWithin || vm.NowPlaying.IsUserSeeking || _isDraggingSeek))
        {
            var ts = TimeSpan.FromSeconds(e.NewValue);
            vm.NowPlaying.CurrentTimeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    private void OnVolumeSliderPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _isDraggingVolume = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingVolume && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingVolume = false;
        }
    }

    private void OnMoreOptionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.ContextMenu != null)
        {
            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.SelectedTrackForContext = Services.Audio.AudioPlayerService.Instance.CurrentTrack;
            }
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnTrackContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm && cm.DataContext is Models.Track track && MainViewModel.Instance != null)
        {
            MainViewModel.Instance.SelectedTrackForContext = track;
        }
    }

    private void OnAddToPlaylistSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem parentItem)
        {
            MainWindow.PopulateAddToPlaylistMenu(parentItem);
        }
    }

    private void OnQueueItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Queue.SelectedTrack != null)
        {
            vm.Queue.PlayTrackCommand.Execute(vm.Queue.SelectedTrack);
        }
    }
}

