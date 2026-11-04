using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Online;
using BenyComes.ViewModels;
using Wpf.Ui.Controls;

namespace BenyComes.Views;

public partial class InfoArtista : UserControl
{
    public static readonly DependencyProperty ArtistProperty =
        DependencyProperty.Register(
            nameof(Artist),
            typeof(ArtistModel),
            typeof(InfoArtista),
            new PropertyMetadata(null, OnArtistPropertyChanged));

    public static readonly DependencyProperty PlayArtistCommandProperty =
        DependencyProperty.Register(nameof(PlayArtistCommand), typeof(ICommand), typeof(InfoArtista), new PropertyMetadata(null));

    public static readonly DependencyProperty PlayTrackCommandProperty =
        DependencyProperty.Register(nameof(PlayTrackCommand), typeof(ICommand), typeof(InfoArtista), new PropertyMetadata(null));

    public static readonly DependencyProperty TrackContextMenuProperty =
        DependencyProperty.Register(nameof(TrackContextMenu), typeof(ContextMenu), typeof(InfoArtista), new PropertyMetadata(null));

    public static readonly DependencyProperty IsGridViewProperty =
        DependencyProperty.Register(nameof(IsGridView), typeof(bool), typeof(InfoArtista), new PropertyMetadata(false));

    public static readonly DependencyProperty IsCompactHeaderProperty =
        DependencyProperty.Register(nameof(IsCompactHeader), typeof(bool), typeof(InfoArtista), new PropertyMetadata(false));

    public static readonly DependencyProperty IsBioPopupOpenProperty =
        DependencyProperty.Register(nameof(IsBioPopupOpen), typeof(bool), typeof(InfoArtista), new PropertyMetadata(false));

    public ArtistModel? Artist
    {
        get => (ArtistModel?)GetValue(ArtistProperty);
        set => SetValue(ArtistProperty, value);
    }

    public ICommand? PlayArtistCommand
    {
        get => (ICommand?)GetValue(PlayArtistCommandProperty);
        set => SetValue(PlayArtistCommandProperty, value);
    }

    public ICommand? PlayTrackCommand
    {
        get => (ICommand?)GetValue(PlayTrackCommandProperty);
        set => SetValue(PlayTrackCommandProperty, value);
    }

    public ContextMenu? TrackContextMenu
    {
        get => (ContextMenu?)GetValue(TrackContextMenuProperty);
        set => SetValue(TrackContextMenuProperty, value);
    }

    public bool IsGridView
    {
        get => (bool)GetValue(IsGridViewProperty);
        set => SetValue(IsGridViewProperty, value);
    }

    public bool IsCompactHeader
    {
        get => (bool)GetValue(IsCompactHeaderProperty);
        set => SetValue(IsCompactHeaderProperty, value);
    }

    public bool IsBioPopupOpen
    {
        get => (bool)GetValue(IsBioPopupOpenProperty);
        set => SetValue(IsBioPopupOpenProperty, value);
    }

    private long _lastHeaderToggleTick;
    private long _artistOpenedTick;

    public InfoArtista()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _artistOpenedTick = Environment.TickCount64;
        }
    }

    private static void OnArtistPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InfoArtista control) return;
        control.IsCompactHeader = false;
        control.IsBioPopupOpen = false;
        control.ListScrollViewer?.ScrollToTop();
        control.GridScrollViewer?.ScrollToTop();

        if (e.NewValue is ArtistModel artist)
        {
            artist.EnsureAlbumsAndStats();
            Favoritos.Instance.SyncTracksFavoriteState(artist.Tracks);

            // Trigger local avatar load & accented color extraction
            var currentImg = artist.AvatarImageSource;
            if (currentImg != null && artist.DominantHeaderBrush == null)
            {
                artist.DominantHeaderBrush = Services.ThemeService.CreateAccentedBoxBrush(currentImg);
            }

            control._artistOpenedTick = Environment.TickCount64;
            control.PlayEntranceAnimation();

            // Fetch artist image & Last.fm biography on-demand if not yet cached
            _ = FetchOnlineArtistDataAsync(artist);
        }
    }

    private void PlayEntranceAnimation()
    {
        if (RootContentGrid == null || RootSlideTransform == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var slideAnim = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };

        RootContentGrid.BeginAnimation(OpacityProperty, fadeAnim);
        RootSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void PlayViewSwitchAnimation(bool isGrid)
    {
        var targetScroll = isGrid ? GridScrollViewer : ListScrollViewer;
        var targetTransform = isGrid ? GridViewSlideTransform : ListViewSlideTransform;
        if (targetScroll == null || targetTransform == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease };
        var slideAnim = new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease };

        targetScroll.BeginAnimation(OpacityProperty, fadeAnim);
        targetTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void PlayBioPopupOpenAnimation()
    {
        if (BioPopupBackdropGrid == null || BioPopupCardBorder == null || BioPopupCardScale == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var backdropFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
        var cardFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        var scaleAnim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };

        BioPopupBackdropGrid.BeginAnimation(OpacityProperty, backdropFade);
        BioPopupCardBorder.BeginAnimation(OpacityProperty, cardFade);
        BioPopupCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        BioPopupCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private static async Task FetchOnlineArtistDataAsync(ArtistModel artist)
    {
        try
        {
            string folder = LastFmService.SanitizeFolderName(artist.Name);
            string artistDir = Path.Combine(LastFmService.ArtistCacheBaseDirectory, folder);
            var localImages = LastFmService.Instance.GetLocalArtistImages(artistDir);

            if (localImages.Count > 0 && File.Exists(localImages[0]))
            {
                if (!artist.HasCustomAvatar || artist.AvatarImage == null)
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        artist.RefreshAvatar(localImages[0]);
                    });
                }
            }
            else
            {
                string? downloadedPath = await LastFmService.Instance
                    .FetchArtistThumbnailOnDemandAsync(artist.Name, artist.AltNames)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        artist.RefreshAvatar(downloadedPath);
                    });
                }
            }

            // Fetch Last.fm biography (summary + full) on-demand
            var (summary, full) = await LastFmService.Instance
                .FetchArtistBioOnDemandAsync(artist.Name, artist.AltNames)
                .ConfigureAwait(false);

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    artist.BiographySummary = summary;
                    artist.BiographyFull = !string.IsNullOrWhiteSpace(full) ? full : summary;
                    artist.HasBiography = true;
                }
                else if (string.IsNullOrWhiteSpace(artist.BiographySummary))
                {
                    string fallbackBio = $"{artist.Name} cuenta con {artist.PrimaryStatsText}{artist.DurationStatsText} en tu biblioteca ({artist.MainGenreDisplay}). No se encontró una biografía detallada registrada en Last.fm para este artista.";
                    artist.BiographySummary = fallbackBio;
                    artist.BiographyFull = fallbackBio;
                    artist.HasBiography = true;
                }
            });
        }
        catch { }
    }

    private void OnOpenBioPopupClick(object sender, MouseButtonEventArgs e)
    {
        if (Artist != null && Artist.HasBiography)
        {
            IsBioPopupOpen = true;
            PlayBioPopupOpenAnimation();
            e.Handled = true;
        }
    }

    private void OnOpenBioPopupMenuClick(object sender, RoutedEventArgs e)
    {
        if (Artist != null)
        {
            if (string.IsNullOrWhiteSpace(Artist.BiographyFull))
            {
                Artist.BiographyFull = !string.IsNullOrWhiteSpace(Artist.BiographySummary)
                    ? Artist.BiographySummary
                    : $"No hay biografía disponible en Last.fm para {Artist.Name}.";
            }
            IsBioPopupOpen = true;
            PlayBioPopupOpenAnimation();
        }
    }

    private void OnCloseBioPopupClick(object sender, RoutedEventArgs e)
    {
        IsBioPopupOpen = false;
        e.Handled = true;
    }

    private void OnBioPopupBackdropClick(object sender, MouseButtonEventArgs e)
    {
        IsBioPopupOpen = false;
        e.Handled = true;
    }

    private void OnBioPopupCardClick(object sender, MouseButtonEventArgs e)
    {
        // Prevent click inside modal card from closing the backdrop
        e.Handled = true;
    }

    private void OnPlayAllArtistClick(object sender, RoutedEventArgs e)
    {
        if (Artist?.Tracks == null || Artist.Tracks.Count == 0) return;
        AudioPlayerService.Instance.IsShuffle = false;
        AudioPlayerService.Instance.PlayQueue(Artist.Tracks, 0);
        if (MainViewModel.Instance != null)
        {
            MainViewModel.Instance.IsShuffleEnabled = false;
            MainViewModel.Instance.ForceUiPlaybackUpdate(Artist.Tracks[0], true);
        }
    }

    private void OnShuffleArtistClick(object sender, RoutedEventArgs e)
    {
        if (Artist?.Tracks == null || Artist.Tracks.Count == 0) return;
        var shuffled = new List<Track>(Artist.Tracks);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        AudioPlayerService.Instance.IsShuffle = true;
        AudioPlayerService.Instance.PlayQueue(shuffled, 0);
        if (MainViewModel.Instance != null)
        {
            MainViewModel.Instance.IsShuffleEnabled = true;
            MainViewModel.Instance.ForceUiPlaybackUpdate(shuffled[0], true);
        }
    }

    private void OnAddToButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn || Artist?.Tracks == null || Artist.Tracks.Count == 0) return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        var queueItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Cola de reproducción",
            Icon = new SymbolIcon { Symbol = SymbolRegular.TextBulletListAdd24 }
        };
        queueItem.Click += (_, _) =>
        {
            AudioPlayerService.Instance.AddRangeToQueue(Artist.Tracks, playFirst: false);
            MainViewModel.Instance?.ShowToast("Artista en cola", $"Se agregaron {Artist.Tracks.Count} canción(es) de {Artist.Name} a la cola.");
        };
        menu.Items.Add(queueItem);

        var favItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Favoritos",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Heart24 }
        };
        favItem.Click += async (_, _) =>
        {
            foreach (var t in Artist.Tracks)
            {
                if (!t.IsFavorite)
                {
                    await Favoritos.Instance.ToggleFavoriteAsync(t);
                }
            }
            MainViewModel.Instance?.Favorites.RefreshData();
            MainViewModel.Instance?.ShowToast("Favoritos", $"Se agregaron las pistas de \"{Artist.Name}\" a Favoritos.");
        };
        menu.Items.Add(favItem);

        menu.Items.Add(new Separator());

        var newPlItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Nueva lista de reproducción...",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 }
        };
        newPlItem.Click += (_, _) =>
        {
            MainViewModel.Instance?.OpenCreatePlaylistDialogCommand.Execute(Artist.Tracks.FirstOrDefault());
        };
        menu.Items.Add(newPlItem);

        var userPlaylists = MainViewModel.Instance?.Playlists.Playlists;
        if (userPlaylists != null && userPlaylists.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var pl in userPlaylists)
            {
                var plItem = new Wpf.Ui.Controls.MenuItem
                {
                    Header = pl.Name,
                    Icon = new SymbolIcon { Symbol = SymbolRegular.MusicNote224 }
                };
                var targetPlaylist = pl;
                plItem.Click += async (_, _) =>
                {
                    foreach (var t in Artist.Tracks)
                    {
                        if (t.Id <= 0)
                        {
                            await LibraryDao.Instance.InsertOrUpdateTrackAsync(t);
                        }
                        if (t.Id > 0)
                        {
                            await PlaylistDao.Instance.AddTrackToPlaylistAsync(targetPlaylist.Id, t.Id);
                        }
                    }
                    if (MainViewModel.Instance != null)
                    {
                        await MainViewModel.Instance.Playlists.LoadPlaylistsAsync();
                        MainViewModel.Instance.ShowToast("Lista actualizada", $"Canciones de \"{Artist.Name}\" agregadas a \"{targetPlaylist.Name}\".");
                    }
                };
                menu.Items.Add(plItem);
            }
        }

        btn.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnArtistPhotoMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnListViewModeClick(object sender, RoutedEventArgs e)
    {
        if (IsGridView)
        {
            IsGridView = false;
            PlayViewSwitchAnimation(false);
        }
    }

    private void OnGridViewModeClick(object sender, RoutedEventArgs e)
    {
        if (!IsGridView)
        {
            IsGridView = true;
            PlayViewSwitchAnimation(true);
        }
    }

    private void OnPlayAlbumButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AlbumModel album && album.Tracks.Count > 0)
        {
            AudioPlayerService.Instance.IsShuffle = false;
            AudioPlayerService.Instance.PlayQueue(album.Tracks, 0);
            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.IsShuffleEnabled = false;
                MainViewModel.Instance.ForceUiPlaybackUpdate(album.Tracks[0], true);
            }
            e.Handled = true;
        }
    }

    private void OnTrackArtistButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Track track && !string.IsNullOrWhiteSpace(track.Artist))
        {
            Helpers.ArtistNavigationHelper.HandleArtistClick(fe, track.Artist);
        }
        e.Handled = true;
    }

    private void OnMoreInfoLastFmClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Artist == null || string.IsNullOrWhiteSpace(Artist.Name)) return;
        try
        {
            string url = $"https://www.last.fm/music/{Uri.EscapeDataString(Artist.Name.Trim())}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOpenAlbumDetailClick(object sender, MouseButtonEventArgs e)
    {
        // Debounce: Evitar clicks accidentales o dobles clicks provenientes de la vista anterior (bloquear los primeros 900ms o click múltiple)
        if (Environment.TickCount64 - _artistOpenedTick < 900 || e.ClickCount > 1)
        {
            e.Handled = true;
            return;
        }

        // Ignore clicks that originated inside the Play button overlay
        if (e.OriginalSource is DependencyObject orig)
        {
            var current = orig;
            while (current != null && current != sender)
            {
                if (current is System.Windows.Controls.Button)
                    return;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        if (sender is FrameworkElement fe && fe.DataContext is AlbumModel album)
        {
            var vm = MainViewModel.Instance;
            if (vm == null) return;

            if (vm.CurrentView == "OsuLibrary")
            {
                vm.PushCurrentNavigationState();
                vm.OsuLibrary.SelectedArtistDetail = null;
                vm.OsuLibrary.SelectedAlbumDetail = album;
            }
            else if (vm.CurrentView == "Favorites")
            {
                vm.PushCurrentNavigationState();
                vm.Favorites.SelectedArtistDetail = null;
                vm.Favorites.SelectedAlbumDetail = album;
            }
            else
            {
                vm.MyMusic.OpenAlbumDetail(album);
            }
            e.Handled = true;
        }
    }

    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Artist == null || IsBioPopupOpen) return;

        var activeScroll = IsGridView ? GridScrollViewer : ListScrollViewer;
        double offset = activeScroll?.VerticalOffset ?? 0.0;

        if (e.Delta < 0 && !IsCompactHeader)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = true;
        }
        else if (e.Delta > 0 && IsCompactHeader && offset <= 24.0)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = false;
        }
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Artist == null || IsBioPopupOpen) return;
        if (Math.Abs(e.ViewportHeightChange) > 0.5 && Math.Abs(e.VerticalChange) < 0.5)
            return;

        if (Environment.TickCount64 - _lastHeaderToggleTick < 180)
            return;

        if (!IsCompactHeader && e.VerticalChange > 0 && e.VerticalOffset > 12.0)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = true;
        }
        else if (IsCompactHeader && e.VerticalOffset <= 6.0 && e.VerticalChange <= 0)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = false;
        }
    }

    private void OnInnerTracksPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled && sender is UIElement element)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = VisualTreeHelper.GetParent(element) as UIElement;
            parent?.RaiseEvent(eventArg);
        }
    }
}
