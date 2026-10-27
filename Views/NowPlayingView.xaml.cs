using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BenyComes.Models;
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
        IsVisibleChanged += OnIsVisibleChanged;
        Services.Audio.AudioPlayerService.Instance.TrackChanged += OnAudioTrackChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _hudInactivityTimer.Start();
            StartBackgroundCinematicMotion();
            if (DataContext is MainViewModel vm)
            {
                AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: true);
            }
        }
        else
        {
            _hudInactivityTimer.Stop();
        }
    }

    private void OnAudioTrackChanged(object? sender, Track? track)
    {
        if (track == null) return;
        StartBackgroundCinematicMotion();
        if (DataContext is MainViewModel vm && vm.IsNowPlayingQueueExpanded)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollToCurrentTrack(center: true)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
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
            AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: true);
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
            sender is MainViewModel vm)
        {
            AnimateQueueState(vm.IsNowPlayingQueueExpanded, immediate: false);
        }
    }

    private void AnimateQueueState(bool isExpanded, bool immediate)
    {
        if (QueueExpandedContainer == null || NormalImmersiveContainer == null || QueueBackdropOverlay == null)
            return;

        if (immediate)
        {
            if (isExpanded)
            {
                NormalImmersiveContainer.Visibility = Visibility.Collapsed;
                NormalImmersiveContainer.Opacity = 0.0;
                QueueBackdropOverlay.Visibility = Visibility.Visible;
                QueueBackdropOverlay.Opacity = 0.9;
                QueueExpandedContainer.Visibility = Visibility.Visible;
                QueueExpandedContainer.Opacity = 1.0;
                QueueTranslate.Y = 0;
            }
            else
            {
                QueueExpandedContainer.Visibility = Visibility.Collapsed;
                QueueExpandedContainer.Opacity = 0.0;
                QueueBackdropOverlay.Visibility = Visibility.Collapsed;
                QueueBackdropOverlay.Opacity = 0.0;
                NormalImmersiveContainer.Visibility = Visibility.Visible;
                NormalImmersiveContainer.Opacity = 1.0;
                NormalImmersiveTranslate.Y = 0;
            }
            return;
        }

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (isExpanded)
        {
            FadeInHud();

            var fadeOutNormal = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
            fadeOutNormal.Completed += (s, e) => NormalImmersiveContainer.Visibility = Visibility.Collapsed;
            NormalImmersiveContainer.BeginAnimation(OpacityProperty, fadeOutNormal);

            QueueBackdropOverlay.Visibility = Visibility.Visible;
            QueueBackdropOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 0.9, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });

            QueueExpandedContainer.Visibility = Visibility.Visible;
            QueueExpandedContainer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut });
            QueueTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(80.0, 0.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeOut });

            Dispatcher.BeginInvoke(new Action(() => ScrollToCurrentTrack(center: true)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            // Ocultar cola animado: slide out downwards y fade out de la cola, y fade in suave de la vista normal
            var fadeOutQueue = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn };
            var slideOutQueue = new DoubleAnimation(0.0, 80.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn };
            fadeOutQueue.Completed += (s, e) =>
            {
                QueueExpandedContainer.Visibility = Visibility.Collapsed;
                QueueBackdropOverlay.Visibility = Visibility.Collapsed;
            };
            QueueExpandedContainer.BeginAnimation(OpacityProperty, fadeOutQueue);
            QueueTranslate.BeginAnimation(TranslateTransform.YProperty, slideOutQueue);

            QueueBackdropOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeIn });

            NormalImmersiveContainer.Visibility = Visibility.Visible;
            NormalImmersiveContainer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });
            NormalImmersiveTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-20.0, 0.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easeOut });
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

        int index = -1;
        for (int i = 0; i < QueueListView.Items.Count; i++)
        {
            if (QueueListView.Items[i] is Track t && (t.Id == current.Id || t.FilePath == current.FilePath))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        QueueListView.ScrollIntoView(QueueListView.Items[index]);
        if (!center) return;

        QueueListView.UpdateLayout();
        var sv = FindScrollViewer(QueueListView);
        if (sv == null) return;

        if (QueueListView.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
        {
            var transform = item.TransformToAncestor(sv);
            Point pt = transform.Transform(new Point(0, 0));
            double itemAbsoluteTop = sv.VerticalOffset + pt.Y;
            double itemCenter = itemAbsoluteTop + (item.ActualHeight / 2.0);
            double viewportHalf = sv.ViewportHeight / 2.0;
            double targetOffset = itemCenter - viewportHalf;
            double maxOffset = Math.Max(0, sv.ScrollableHeight);
            targetOffset = Math.Clamp(targetOffset, 0, maxOffset);

            sv.ScrollToVerticalOffset(targetOffset);
        }
        else if (sv.CanContentScroll)
        {
            double viewportHalf = sv.ViewportHeight / 2.0;
            double targetOffset = Math.Clamp(index - viewportHalf, 0, Math.Max(0, sv.ScrollableHeight));
            sv.ScrollToVerticalOffset(targetOffset);
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

        StartBackgroundCinematicMotion();
    }

    private void StartBackgroundCinematicMotion()
    {
        var duration = TimeSpan.FromSeconds(24);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        var scaleAnim = new DoubleAnimation(1.02, 1.14, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        };

        var panAnim = new DoubleAnimation(-20.0, 20.0, duration)
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

