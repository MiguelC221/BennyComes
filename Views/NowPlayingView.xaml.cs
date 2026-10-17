using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BenyComes.ViewModels;

namespace BenyComes.Views;

public partial class NowPlayingView : UserControl
{
    private bool _isPrimaryActive = true;
    private readonly System.Windows.Threading.DispatcherTimer _hudInactivityTimer;
    private bool _isHudVisible = true;

    public NowPlayingView()
    {
        InitializeComponent();

        _hudInactivityTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _hudInactivityTimer.Tick += OnInactivityTimerTick;

        PreviewMouseMove += OnUserActivity;
        PreviewMouseDown += OnUserActivity;
        PreviewKeyDown += OnUserActivity;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hudInactivityTimer.Start();

        if (DataContext is MainViewModel vm && vm.NowPlaying != null)
        {
            vm.NowPlaying.PropertyChanged -= OnNowPlayingPropertyChanged;
            vm.NowPlaying.PropertyChanged += OnNowPlayingPropertyChanged;
            vm.NowPlaying.OnBackgroundTransitionRequested -= OnBackgroundTransitionRequested;
            vm.NowPlaying.OnBackgroundTransitionRequested += OnBackgroundTransitionRequested;
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;

            if (vm.NowPlaying.BackgroundImage != null)
            {
                BackgroundPrimary.Source = vm.NowPlaying.BackgroundImage;
            }

            _ = vm.NowPlaying.RefreshArtistBackgroundAsync(vm.NowPlaying.Artist);
        }

        StartBackgroundCinematicMotion();
    }

    private void OnUserActivity(object? sender, EventArgs e)
    {
        _hudInactivityTimer.Stop();
        if (!_isHudVisible)
        {
            FadeInHud();
        }
        _hudInactivityTimer.Start();
    }

    private void OnInactivityTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingSeek || _isDraggingVolume) return;
        if (DataContext is MainViewModel vm && vm.IsNowPlayingQueueExpanded) return;

        if (_isHudVisible)
        {
            FadeOutHud();
        }
    }

    private void FadeOutHud()
    {
        _isHudVisible = false;
        var duration = TimeSpan.FromMilliseconds(500);
        var moveDuration = TimeSpan.FromMilliseconds(600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

        TopHudContainer.BeginAnimation(OpacityProperty, fadeOut);
        BottomHudControlsContainer.BeginAnimation(OpacityProperty, fadeOut);
        TechBadgesPanel.BeginAnimation(OpacityProperty, fadeOut);
        TopHudContainer.IsHitTestVisible = false;
        BottomHudControlsContainer.IsHitTestVisible = false;
        TechBadgesPanel.IsHitTestVisible = false;

        // Measured layout: the hidden controls still reserve space, so drop everything to the window foot.
        const double footMargin = 16;
        double controlsHeight = BottomHudControlsContainer.ActualHeight;
        double rowBottomGap = 20; // bottom margin of the immersive grid

        double trackDrop = Math.Max(0, controlsHeight + VisualizerContainer.ActualHeight + VisualizerContainer.Margin.Bottom
                                        + rowBottomGap + 24 - footMargin);
        TrackInfoTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(TrackInfoTranslate.Y, trackDrop, moveDuration) { EasingFunction = easeInOut });

        // Visualizer: bottom-right corner of the window
        FrameworkElement visual = WaveformLine.Visibility == Visibility.Visible ? WaveformLine : SpectrumItems;
        double visualWidth = visual.ActualWidth;
        double shiftX = Math.Max(0, (VisualizerContainer.ActualWidth - visualWidth) / 2.0);
        double dropY = Math.Max(0, controlsHeight + VisualizerContainer.Margin.Bottom + rowBottomGap - footMargin);
        VisualizerTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(VisualizerTranslate.X, shiftX, moveDuration) { EasingFunction = easeInOut });
        VisualizerTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(VisualizerTranslate.Y, dropY, moveDuration) { EasingFunction = easeInOut });
    }

    private void FadeInHud()
    {
        _isHudVisible = true;
        var duration = TimeSpan.FromMilliseconds(300);
        var returnDuration = TimeSpan.FromMilliseconds(450);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };

        TopHudContainer.BeginAnimation(OpacityProperty, fadeIn);
        BottomHudControlsContainer.BeginAnimation(OpacityProperty, fadeIn);
        TechBadgesPanel.BeginAnimation(OpacityProperty, fadeIn);
        TopHudContainer.IsHitTestVisible = true;
        BottomHudControlsContainer.IsHitTestVisible = true;
        TechBadgesPanel.IsHitTestVisible = true;

        // Restore track info smoothly to original position
        var restoreTrackInfo = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
        TrackInfoTranslate.BeginAnimation(TranslateTransform.YProperty, restoreTrackInfo);

        // Restore visualizer smoothly to original position
        var restoreVisualizerX = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
        var restoreVisualizerY = new DoubleAnimation(0.0, returnDuration) { EasingFunction = ease };
        VisualizerTranslate.BeginAnimation(TranslateTransform.XProperty, restoreVisualizerX);
        VisualizerTranslate.BeginAnimation(TranslateTransform.YProperty, restoreVisualizerY);
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsNowPlayingQueueExpanded) &&
            sender is MainViewModel vm && vm.IsNowPlayingQueueExpanded)
        {
            FadeInHud();
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

    private void OnBlurSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PrimaryBlur != null) PrimaryBlur.Radius = e.NewValue;
        if (SecondaryBlur != null) SecondaryBlur.Radius = e.NewValue;
    }

    private void OnDarknessSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundDarknessOverlay != null)
        {
            BackgroundDarknessOverlay.Opacity = e.NewValue;
        }
    }

    private void OnNowPlayingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundImage) && DataContext is MainViewModel vm)
        {
            if (!vm.NowPlaying.IsArtistPhotoActive && vm.NowPlaying.BackgroundImage != null)
            {
                CrossfadeBackgroundWithKenBurns(vm.NowPlaying.BackgroundImage, false);
            }
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.BackgroundBlurRadius) && DataContext is MainViewModel vmBlur)
        {
            if (PrimaryBlur != null) PrimaryBlur.Radius = vmBlur.NowPlaying.BackgroundBlurRadius;
            if (SecondaryBlur != null) SecondaryBlur.Radius = vmBlur.NowPlaying.BackgroundBlurRadius;
        }
    }

    private void OnBackgroundTransitionRequested(BitmapImage newImage, bool isArtistPhoto)
    {
        CrossfadeBackgroundWithKenBurns(newImage, isArtistPhoto);
    }

    private void CrossfadeBackgroundWithKenBurns(BitmapImage newImage, bool isArtistPhoto)
    {
        var duration = TimeSpan.FromMilliseconds(1000);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        double targetBlur = (DataContext is MainViewModel vm) ? vm.NowPlaying.BackgroundBlurRadius : 0;

        if (_isPrimaryActive)
        {
            BackgroundSecondary.Source = newImage;
            SecondaryBlur.Radius = targetBlur;

            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                if (!_isPrimaryActive)
                {
                    BackgroundPrimary.Source = null;
                }
            };

            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = false;
        }
        else
        {
            BackgroundPrimary.Source = newImage;
            PrimaryBlur.Radius = targetBlur;

            var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
            fadeOut.Completed += (s, e) =>
            {
                if (_isPrimaryActive)
                {
                    BackgroundSecondary.Source = null;
                }
            };

            BackgroundPrimary.BeginAnimation(OpacityProperty, fadeIn);
            BackgroundSecondary.BeginAnimation(OpacityProperty, fadeOut);
            _isPrimaryActive = true;
        }
    }

    private void StartBackgroundCinematicMotion()
    {
        var duration = TimeSpan.FromSeconds(45);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        var scaleAnim = new DoubleAnimation(1.0, 1.12, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        var panAnim = new DoubleAnimation(-25.0, 25.0, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        BackgroundScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        BackgroundScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        BackgroundTranslate.BeginAnimation(TranslateTransform.YProperty, panAnim);
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

