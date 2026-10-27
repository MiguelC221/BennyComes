using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BenyComes.ViewModels;

namespace BenyComes;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        App.LogMessage("MainWindow constructor start");
        try
        {
            InitializeComponent();
            App.LogMessage("MainWindow InitializeComponent finished");
        }
        catch (Exception ex)
        {
            App.LogMessage($"MainWindow InitializeComponent EXCEPTION: {ex}");
            throw;
        }

        RestoreWindowBounds();

        Loaded += (s, e) =>
        {
            App.LogMessage("MainWindow Loaded event fired");
            SetupTrackChangeFadeAnimation();
        };
        Closing += (s, e) =>
        {
            App.LogMessage($"MainWindow Closing event fired, Cancel={e.Cancel}");
            SaveWindowBounds();
            try
            {
                ViewModel.SavePlaybackStateAsync().GetAwaiter().GetResult();
            }
            catch { }
        };
        Closed += (s, e) => App.LogMessage("MainWindow Closed event fired");
    }

    private void SetupTrackChangeFadeAnimation()
    {
        Services.Audio.AudioPlayerService.Instance.TrackChanged += (s, track) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MiniPlayerBigButton != null)
                {
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(500))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    fadeOut.Completed += (s, ev) =>
                    {
                        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(500))
                        {
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };
                        MiniPlayerBigButton.BeginAnimation(OpacityProperty, fadeIn);
                    };
                    MiniPlayerBigButton.BeginAnimation(OpacityProperty, fadeOut);
                }
            });
        };
    }

    private sealed class WindowBoundsState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static string WindowStatePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BenyComes", "window_state.json");

    private void RestoreWindowBounds()
    {
        try
        {
            if (!System.IO.File.Exists(WindowStatePath)) return;
            var st = System.Text.Json.JsonSerializer.Deserialize<WindowBoundsState>(System.IO.File.ReadAllText(WindowStatePath));
            if (st == null || st.Width < MinWidth || st.Height < MinHeight) return;

            // Solo restaurar si la ventana queda visible en el escritorio virtual actual
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
            bool visible = st.Left + st.Width > vl + 80 && st.Left < vr - 80 && st.Top >= vt - 10 && st.Top < vb - 80;
            if (!visible) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = st.Left;
            Top = st.Top;
            Width = st.Width;
            Height = st.Height;
            if (st.Maximized) WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            App.LogMessage($"RestoreWindowBounds failed: {ex.Message}");
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            // RestoreBounds da el tamaño "normal" aunque esté maximizada/minimizada
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var st = new WindowBoundsState
            {
                Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
                Maximized = WindowState == WindowState.Maximized
            };
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowStatePath)!);
            System.IO.File.WriteAllText(WindowStatePath, System.Text.Json.JsonSerializer.Serialize(st));
        }
        catch (Exception ex)
        {
            App.LogMessage($"SaveWindowBounds failed: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(dep) != null)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                if (WindowState == WindowState.Maximized)
                {
                    // Obtener la posición del cursor respecto a la ventana antes de restaurar
                    Point curPoint = e.GetPosition(this);
                    double percentX = Math.Clamp(curPoint.X / Math.Max(1.0, ActualWidth), 0.05, 0.95);

                    // Restaurar ventana
                    WindowState = WindowState.Normal;

                    // Posicionar la ventana centrada proporcionalmente bajo el cursor
                    double targetLeft = curPoint.X - (ActualWidth * percentX);
                    Left = Math.Max(0, targetLeft);
                    Top = Math.Max(0, curPoint.Y - 18);
                }

                try
                {
                    var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
                    if (source != null)
                    {
                        ReleaseCapture();
                        SendMessage(source.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                    }
                    else
                    {
                        DragMove();
                    }
                }
                catch
                {
                    try { DragMove(); } catch { }
                }
            }
        }
    }

    private bool _isDraggingSeek;
    private bool _isDraggingVolume;
    private bool _wasNarrowWindow;

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        bool isNarrow = e.NewSize.Width < 1080;
        if (isNarrow && !_wasNarrowWindow)
        {
            _wasNarrowWindow = true;
            vm.IsNavPaneOpen = false;
        }
        else if (!isNarrow && _wasNarrowWindow)
        {
            _wasNarrowWindow = false;
            vm.IsNavPaneOpen = true;
        }
    }

    private void OnDismissDialogClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsDeviceSelectorOpen = false;
    }

    private void OnDismissEqualizerClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEqualizerOpen = false;
    }

    private void OnTrackContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm && cm.DataContext is Models.Track track)
        {
            ViewModel.SelectedTrackForContext = track;
        }
    }

    private void OnMiniPlayerContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var track = ViewModel?.CurrentTrack ?? Services.Audio.AudioPlayerService.Instance.CurrentTrack;
        if (ViewModel != null && track != null)
        {
            ViewModel.SelectedTrackForContext = track;
        }
    }

    private void OnAddToPlaylistSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem parentItem)
        {
            PopulateAddToPlaylistMenu(parentItem);
        }
    }

    public static void PopulateAddToPlaylistMenu(MenuItem parentItem)
    {
        parentItem.Items.Clear();

        if (MainViewModel.Instance == null) return;

        // 1. + Nueva lista de reproducción...
        var newPlItem = new MenuItem
        {
            Header = "+ Nueva lista de reproducción..."
        };
        newPlItem.Click += (s, e) =>
        {
            MainViewModel.Instance?.OpenCreatePlaylistDialog(MainViewModel.Instance.SelectedTrackForContext);
        };
        parentItem.Items.Add(newPlItem);

        parentItem.Items.Add(new Separator());

        // 2. Playlists creadas dinámicamente
        var playlists = MainViewModel.Instance.Playlists.Playlists;
        if (playlists.Count == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = "(No hay listas creadas)",
                IsEnabled = false
            };
            parentItem.Items.Add(emptyItem);
        }
        else
        {
            foreach (var p in playlists)
            {
                var pItem = new MenuItem
                {
                    Header = p.Name
                };
                var captured = p;
                pItem.Click += async (s, e) =>
                {
                    await MainViewModel.Instance.AddTrackToPlaylistWithCheckAsync(captured);
                };
                parentItem.Items.Add(pItem);
            }
        }
    }

    private void OnMoreOptionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.ContextMenu != null)
        {
            ViewModel.SelectedTrackForContext = Services.Audio.AudioPlayerService.Instance.CurrentTrack;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private static void SetSliderValueFromMousePosition(Slider slider, MouseEventArgs e)
    {
        if (slider.ActualWidth <= 0) return;
        Point pt = e.GetPosition(slider);
        double ratio = Math.Clamp(pt.X / slider.ActualWidth, 0.0, 1.0);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
    }

    private void OnMiniSeekContainerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (MiniSeekSlider != null)
        {
            _isDraggingSeek = true;
            ViewModel.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(MiniSeekSlider, e);
        }
    }

    private void OnMiniSeekContainerMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == MouseButtonState.Pressed && MiniSeekSlider != null)
        {
            SetSliderValueFromMousePosition(MiniSeekSlider, e);
        }
    }

    private void OnMiniSeekContainerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSeek && MiniSeekSlider != null)
        {
            SetSliderValueFromMousePosition(MiniSeekSlider, e);
            _isDraggingSeek = false;
            ViewModel.NowPlaying.SeekCommand.Execute(MiniSeekSlider.Value);
            ViewModel.NowPlaying.IsUserSeeking = false;
        }
    }

    private void OnSeekSliderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _isDraggingSeek = true;
            ViewModel.NowPlaying.IsUserSeeking = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnSeekSliderPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingSeek = false;
            ViewModel.NowPlaying.SeekCommand.Execute(slider.Value);
        }
        ViewModel.NowPlaying.IsUserSeeking = false;
    }

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && (slider.IsMouseCaptureWithin || ViewModel.NowPlaying.IsUserSeeking || _isDraggingSeek))
        {
            var ts = TimeSpan.FromSeconds(e.NewValue);
            ViewModel.NowPlaying.CurrentTimeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    private void OnVolumeSliderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _isDraggingVolume = true;
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingVolume && e.LeftButton == MouseButtonState.Pressed && sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
        }
    }

    private void OnVolumeSliderPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            SetSliderValueFromMousePosition(slider, e);
            _isDraggingVolume = false;
        }
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                await ViewModel.HandleFilesDroppedAsync(files);
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
        if (source != null)
        {
            source.AddHook(WndProc);
            Services.Audio.SystemMediaTransportControlsService.Instance.InitializeForWindow(source.Handle);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_APPCOMMAND = 0x0319;
        if (msg == WM_APPCOMMAND)
        {
            int cmd = (int)((uint)lParam >> 16) & ~0xF000;
            switch (cmd)
            {
                case 14: // APPCOMMAND_MEDIA_PLAY_PAUSE
                    Services.Audio.AudioPlayerService.Instance.TogglePlayPause();
                    handled = true;
                    break;
                case 11: // APPCOMMAND_MEDIA_NEXTTRACK
                    Services.Audio.AudioPlayerService.Instance.Next();
                    handled = true;
                    break;
                case 12: // APPCOMMAND_MEDIA_PREVIOUSTRACK
                    Services.Audio.AudioPlayerService.Instance.Previous();
                    handled = true;
                    break;
                case 13: // APPCOMMAND_MEDIA_STOP
                    Services.Audio.AudioPlayerService.Instance.Stop();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void OnPlaylistContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe && fe.DataContext != null)
        {
            cm.DataContext = fe.DataContext;
        }
    }

    private void OnPlaylistMoreOptionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.DataContext = fe.DataContext;
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnSidebarSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ViewModel.ExecuteSearchFromHeaderCommand.Execute(null);
            if (sender is UIElement uie)
            {
                uie.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ViewModel.Search.CloseSuggestionsCommand.Execute(null);
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
}