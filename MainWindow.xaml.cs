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

        StateChanged += (s, e) =>
        {
            // Si el usuario desmaximiza (clic en botón Restaurar/Desmaximizar de la barra de título), asegurar salida de fullscreen
            if (WindowState == WindowState.Normal && ViewModel.IsFullscreen)
            {
                ViewModel.IsFullscreen = false;
                WindowStyle = WindowStyle.SingleBorderWindow;
            }
        };

        Loaded += (s, e) =>
        {
            App.LogMessage("MainWindow Loaded event fired");
            SetupTrackChangeFadeAnimation();
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.IsNavPaneOpen))
                    {
                        UpdateNavPaneOverlayMode(ActualWidth);
                    }
                };
            }
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
            Dispatcher.InvokeAsync(async () =>
            {
                if (MiniPlayerBigButton != null)
                {
                    // Animación estilo Groove Music (cinta deslizante hacia la derecha)
                    var slideEaseOut = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                    var slideEaseIn = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };

                    // 1. Limpieza instantánea y salida rápida
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(MiniPlayerBigButton.Opacity, 0.0, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = slideEaseIn
                    };
                    
                    var slideRightOut = new System.Windows.Media.Animation.DoubleAnimation(0.0, 40.0, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = slideEaseIn
                    };

                    MiniPlayerBigButton.BeginAnimation(OpacityProperty, fadeOut);
                    var currentTranslate = (MiniPlayerBigButton.Template?.FindName("MiniTrackInfoTranslate", MiniPlayerBigButton) as System.Windows.Media.TranslateTransform);
                    currentTranslate?.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideRightOut);

                    // 2. Breve pausa en blanco (~180ms) para que todo se limpie antes de entrar la nueva pista
                    await Task.Delay(180);

                    // 3. Entrada suave desde la izquierda estilo cinta
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = slideEaseOut
                    };
                    var slideInFromLeft = new System.Windows.Media.Animation.DoubleAnimation(-35.0, 0.0, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = slideEaseOut
                    };

                    MiniPlayerBigButton.BeginAnimation(OpacityProperty, fadeIn);
                    var translateIn = (MiniPlayerBigButton.Template?.FindName("MiniTrackInfoTranslate", MiniPlayerBigButton) as System.Windows.Media.TranslateTransform);
                    translateIn?.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideInFromLeft);
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

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
                    // Obtener la posición absoluta del cursor en pantalla mediante Win32 (coordenadas de pantalla físicas/virtuales multimonitor)
                    double screenX = 0;
                    double screenY = 0;
                    if (GetCursorPos(out POINT pt))
                    {
                        var source = PresentationSource.FromVisual(this);
                        if (source?.CompositionTarget != null)
                        {
                            var transform = source.CompositionTarget.TransformFromDevice;
                            var wpfPt = transform.Transform(new Point(pt.X, pt.Y));
                            screenX = wpfPt.X;
                            screenY = wpfPt.Y;
                        }
                        else
                        {
                            screenX = pt.X;
                            screenY = pt.Y;
                        }
                    }
                    else
                    {
                        Point curPoint = PointToScreen(e.GetPosition(this));
                        screenX = curPoint.X;
                        screenY = curPoint.Y;
                    }

                    // Porcentaje horizontal donde el usuario hizo clic en la ventana maximizada
                    Point curPointInWindow = e.GetPosition(this);
                    double percentX = Math.Clamp(curPointInWindow.X / Math.Max(1.0, ActualWidth), 0.05, 0.95);

                    // Restaurar ventana
                    WindowState = WindowState.Normal;

                    // Posicionar la ventana bajo el cursor en el monitor actual donde ocurrió el clic
                    Left = screenX - (ActualWidth * percentX);
                    Top = Math.Max(0, screenY - 18);
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

        UpdateNavPaneOverlayMode(e.NewSize.Width);
    }

    private void UpdateNavPaneOverlayMode(double windowWidth)
    {
        if (NavPaneBackdrop == null || SidebarBorder == null || SidebarColumn == null) return;

        // Se considera modo móvil / estrecho cuando el ancho de la ventana es menor a 1080 px
        bool isMobile = windowWidth < 1080;
        ViewModel.IsMobileLayout = isMobile;

        if (isMobile)
        {
            // MODO MÓVIL: La columna base queda fija en 48 px y la barra lateral flota con sombra y backdrop
            SidebarColumn.Width = new GridLength(48);

            if (ViewModel.IsNavPaneOpen)
            {
                NavPaneBackdrop.Visibility = Visibility.Visible;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
                NavPaneBackdrop.BeginAnimation(UIElement.OpacityProperty, anim);
                if (SidebarShadow != null)
                {
                    var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation(0.0, 0.45, TimeSpan.FromMilliseconds(200));
                    SidebarShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);
                }
            }
            else
            {
                if (NavPaneBackdrop.Visibility == Visibility.Visible)
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
                    anim.Completed += (s, e) => { NavPaneBackdrop.Visibility = Visibility.Collapsed; };
                    NavPaneBackdrop.BeginAnimation(UIElement.OpacityProperty, anim);
                }
                if (SidebarShadow != null)
                {
                    var shadowAnim = new System.Windows.Media.Animation.DoubleAnimation(0.45, 0.0, TimeSpan.FromMilliseconds(150));
                    SidebarShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);
                }
            }
        }
        else
        {
            // MODO ESCRITORIO (Fuera de móvil): La barra lateral NO es flotante.
            // Ocupa su espacio propio en el Grid (240 px expandida, 48 px contraída) sin tapar el contenido ni mostrar backdrop
            NavPaneBackdrop.Visibility = Visibility.Collapsed;
            if (SidebarShadow != null)
            {
                SidebarShadow.Opacity = 0;
            }

            double targetWidth = ViewModel.IsNavPaneOpen ? 240 : 48;
            SidebarColumn.Width = new GridLength(targetWidth);
        }
    }

    private void OnNavPaneBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsNavPaneOpen = false;
        UpdateNavPaneOverlayMode(ActualWidth);
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
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
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
            ViewModel.NowPlaying.IsUserSeeking = false;
        }
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

    private void OnMiniPlayerBarPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel?.NowPlaying != null)
        {
            // Subir / Bajar volumen un 3% con la rueda del mouse en el panel principal (MiniPlayer)
            float step = e.Delta > 0 ? 0.03f : -0.03f;
            float newVol = Math.Clamp(ViewModel.NowPlaying.Volume + step, 0.0f, 1.0f);
            ViewModel.NowPlaying.Volume = newVol;
            e.Handled = true;
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

    private void OnSidebarSearchBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Search != null)
        {
            ViewModel.Search.ActiveSearchSource = "Sidebar";
        }
    }

    private void OnSidebarSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel?.Search == null) return;

        ViewModel.Search.ActiveSearchSource = "Sidebar";

        if (e.Key == Key.Down)
        {
            if (ViewModel.Search.IsSidebarSuggestionsOpen)
            {
                e.Handled = true;
                ViewModel.Search.MoveSuggestionSelectionCommand.Execute(1);
            }
        }
        else if (e.Key == Key.Up)
        {
            if (ViewModel.Search.IsSidebarSuggestionsOpen)
            {
                e.Handled = true;
                ViewModel.Search.MoveSuggestionSelectionCommand.Execute(-1);
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (ViewModel.Search.IsSidebarSuggestionsOpen && ViewModel.Search.SelectedSuggestionIndex >= 0)
            {
                ViewModel.Search.SelectCurrentHighlightedSuggestionCommand.Execute(null);
            }
            else
            {
                ViewModel.ExecuteSearchFromHeaderCommand.Execute(null);
            }

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

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        // 0. Si la aplicación está en pantalla completa (Fullscreen), Esc sale inmediatamente de pantalla completa
        if (ViewModel.IsFullscreen)
        {
            ViewModel.ExitFullscreen(this);
            e.Handled = true;
            return;
        }

        // 1. Prioridad: Diálogos modales y paneles emergentes
        if (ViewModel.IsEqualizerOpen)
        {
            ViewModel.IsEqualizerOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsTrackPropertiesModalOpen)
        {
            ViewModel.IsTrackPropertiesModalOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsArtistGalleryModalOpen)
        {
            ViewModel.IsArtistGalleryModalOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsArtistBioModalOpen)
        {
            ViewModel.IsArtistBioModalOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsLicensesModalOpen)
        {
            ViewModel.CloseLicensesModal();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsNewPlaylistDialogVisible)
        {
            ViewModel.CancelNewPlaylistDialog();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsEditPlaylistDialogVisible)
        {
            ViewModel.CancelEditPlaylistDialog();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsDuplicateDialogVisible)
        {
            ViewModel.IsDuplicateDialogVisible = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.Settings.IsSeparatorsModalOpen)
        {
            ViewModel.Settings.CloseSeparatorsModal();
            e.Handled = true;
            return;
        }

        if (ViewModel.Settings.IsExceptionsModalOpen)
        {
            ViewModel.Settings.CloseExceptionsModal();
            e.Handled = true;
            return;
        }

        if (ViewModel.Settings.IsLibraryFoldersModalOpen)
        {
            ViewModel.Settings.CloseLibraryFoldersModal();
            e.Handled = true;
            return;
        }

        if (ViewModel.IsDeviceSelectorOpen)
        {
            ViewModel.IsDeviceSelectorOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsQueueDrawerOpen)
        {
            ViewModel.IsQueueDrawerOpen = false;
            e.Handled = true;
            return;
        }

        if (ViewModel.Search.IsSidebarSuggestionsOpen)
        {
            ViewModel.Search.CloseSuggestionsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // 2. Si está en pantalla inmersiva (Reproducción en curso), salir a la biblioteca
        if (ViewModel.IsNowPlayingActive)
        {
            ViewModel.CloseNowPlaying();
            e.Handled = true;
            return;
        }

        // 3. Volver atrás en el historial de navegación si está disponible
        if (ViewModel.CanGoBack)
        {
            ViewModel.GoBack();
            e.Handled = true;
            return;
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