using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.ViewModels;
using Wpf.Ui.Controls;

namespace BenyComes.Views;

public partial class InfoAlbum : UserControl
{
    public static readonly DependencyProperty AlbumProperty =
        DependencyProperty.Register(
            nameof(Album),
            typeof(AlbumModel),
            typeof(InfoAlbum),
            new PropertyMetadata(null, OnAlbumPropertyChanged));

    public static readonly DependencyProperty OpenArtistByNameCommandProperty =
        DependencyProperty.Register(nameof(OpenArtistByNameCommand), typeof(ICommand), typeof(InfoAlbum), new PropertyMetadata(null));

    public static readonly DependencyProperty PlayTrackCommandProperty =
        DependencyProperty.Register(nameof(PlayTrackCommand), typeof(ICommand), typeof(InfoAlbum), new PropertyMetadata(null));

    public static readonly DependencyProperty TrackContextMenuProperty =
        DependencyProperty.Register(nameof(TrackContextMenu), typeof(ContextMenu), typeof(InfoAlbum), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCompactHeaderProperty =
        DependencyProperty.Register(nameof(IsCompactHeader), typeof(bool), typeof(InfoAlbum), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(InfoAlbum), new PropertyMetadata(false));

    public static readonly DependencyProperty IsImagePreviewOpenProperty =
        DependencyProperty.Register(nameof(IsImagePreviewOpen), typeof(bool), typeof(InfoAlbum), new PropertyMetadata(false));

    public AlbumModel? Album
    {
        get => (AlbumModel?)GetValue(AlbumProperty);
        set => SetValue(AlbumProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public ICommand? OpenArtistByNameCommand
    {
        get => (ICommand?)GetValue(OpenArtistByNameCommandProperty);
        set => SetValue(OpenArtistByNameCommandProperty, value);
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

    public bool IsCompactHeader
    {
        get => (bool)GetValue(IsCompactHeaderProperty);
        set => SetValue(IsCompactHeaderProperty, value);
    }

    public bool IsImagePreviewOpen
    {
        get => (bool)GetValue(IsImagePreviewOpenProperty);
        set => SetValue(IsImagePreviewOpenProperty, value);
    }

    private void OnLateralCoverImageClick(object sender, MouseButtonEventArgs e)
    {
        IsImagePreviewOpen = true;
        e.Handled = true;
    }

    private void OnCloseImagePreviewClick(object sender, RoutedEventArgs e)
    {
        IsImagePreviewOpen = false;
        if (e != null) e.Handled = true;
    }

    private long _lastHeaderToggleTick;
    private double _lastVerticalOffset;

    public InfoAlbum()
    {
        InitializeComponent();
    }

    private static void OnAlbumPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InfoAlbum control) return;
        control.IsCompactHeader = false;
        control.IsImagePreviewOpen = false;
        control._lastVerticalOffset = 0;

        if (e.NewValue is AlbumModel album)
        {
            album.RefreshStats();
            Favoritos.Instance.SyncTracksFavoriteState(album.Tracks);
            // Trigger cover load & accented header brush calculation if not already loaded
            var cover = album.CoverImageSource;
            if (cover != null && album.DominantHeaderBrush == null)
            {
                album.DominantHeaderBrush = Services.ThemeService.CreateAccentedBoxBrush(cover);
            }

            control.PlayEntranceAnimation();
        }
    }

    private void PlayEntranceAnimation()
    {
        if (RootAlbumContentGrid == null || RootAlbumSlideTransform == null) return;

        RootAlbumContentGrid.BeginAnimation(OpacityProperty, null);
        RootAlbumSlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        RootAlbumContentGrid.Opacity = 0.0;
        RootAlbumSlideTransform.Y = 22.0;

        if (AlbumTracksListView != null && AlbumTracksListView.Items.Count > 0)
        {
            AlbumTracksListView.ScrollIntoView(AlbumTracksListView.Items[0]);
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (RootAlbumContentGrid == null || RootAlbumSlideTransform == null) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
            var slideAnim = new DoubleAnimation(22.0, 0.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };

            RootAlbumContentGrid.BeginAnimation(OpacityProperty, fadeAnim);
            RootAlbumSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Album == null) return;

        if (AlbumTracksListView != null && !AlbumTracksListView.IsMouseOver)
        {
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            AlbumTracksListView.RaiseEvent(eventArg);
            e.Handled = true;
        }
    }

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Album == null) return;
        _lastVerticalOffset = e.VerticalOffset;

        if (Math.Abs(e.ViewportHeightChange) > 0.5 && Math.Abs(e.VerticalChange) < 0.5)
            return;

        double totalContent = e.ExtentHeight;
        double viewport = e.ViewportHeight;
        double scrollable = totalContent - viewport;

        bool canCompact = scrollable > 150.0;

        if (!IsCompactHeader)
        {
            if (canCompact && e.VerticalOffset >= 80.0 && e.VerticalChange > 0)
            {
                if (Environment.TickCount64 - _lastHeaderToggleTick >= 200)
                {
                    _lastHeaderToggleTick = Environment.TickCount64;
                    IsCompactHeader = true;
                }
            }
        }
        else
        {
            if (e.VerticalOffset <= 15.0 || scrollable <= 10.0)
            {
                if (Environment.TickCount64 - _lastHeaderToggleTick >= 200)
                {
                    _lastHeaderToggleTick = Environment.TickCount64;
                    IsCompactHeader = false;
                }
            }
        }
    }

    private void NavigateToArtistFromAlbum(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;

        if (OpenArtistByNameCommand?.CanExecute(artistName) == true)
        {
            OpenArtistByNameCommand.Execute(artistName);
        }
        else
        {
            MainViewModel.Instance?.NavigateToArtist(artistName);
        }
    }

    private void OnHeaderArtistButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && !string.IsNullOrWhiteSpace(Album?.Artist))
        {
            Helpers.ArtistNavigationHelper.HandleArtistClick(fe, Album.Artist);
        }
        else
        {
            NavigateToArtistFromAlbum(Album?.Artist);
        }
        e.Handled = true;
    }

    private void OnTrackArtistButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Track track && !string.IsNullOrWhiteSpace(track.Artist))
        {
            Helpers.ArtistNavigationHelper.HandleArtistClick(fe, track.Artist);
        }
        else if (sender is FrameworkElement feFallback && !string.IsNullOrWhiteSpace(Album?.Artist))
        {
            Helpers.ArtistNavigationHelper.HandleArtistClick(feFallback, Album.Artist);
        }
        else
        {
            NavigateToArtistFromAlbum(Album?.Artist);
        }
        e.Handled = true;
    }

    private void OnPlayAllClick(object sender, RoutedEventArgs e)
    {
        if (Album?.Tracks == null || Album.Tracks.Count == 0) return;
        AudioPlayerService.Instance.IsShuffle = false;
        AudioPlayerService.Instance.PlayQueue(Album.Tracks, 0);
        if (MainViewModel.Instance != null)
        {
            MainViewModel.Instance.IsShuffleEnabled = false;
            MainViewModel.Instance.ForceUiPlaybackUpdate(Album.Tracks[0], true);
        }
    }

    private void OnShuffleAlbumClick(object sender, RoutedEventArgs e)
    {
        if (Album?.Tracks == null || Album.Tracks.Count == 0) return;
        var shuffled = new List<Track>(Album.Tracks);
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
        if (sender is not FrameworkElement btn || Album?.Tracks == null || Album.Tracks.Count == 0) return;

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
            AudioPlayerService.Instance.AddRangeToQueue(Album.Tracks, playFirst: false);
            MainViewModel.Instance?.ShowToast("Álbum en cola", $"Se agregaron {Album.Tracks.Count} canción(es) a la cola.");
        };
        menu.Items.Add(queueItem);

        var favItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Favoritos",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Heart24 }
        };
        favItem.Click += async (_, _) =>
        {
            foreach (var t in Album.Tracks)
            {
                if (!t.IsFavorite)
                {
                    await Favoritos.Instance.ToggleFavoriteAsync(t);
                }
            }
            MainViewModel.Instance?.Favorites.RefreshData();
            MainViewModel.Instance?.ShowToast("Favoritos", $"Se agregaron las pistas de \"{Album.Name}\" a Favoritos.");
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
            MainViewModel.Instance?.OpenCreatePlaylistDialogCommand.Execute(Album.Tracks.FirstOrDefault());
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
                    foreach (var t in Album.Tracks)
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
                        MainViewModel.Instance.ShowToast("Lista actualizada", $"Álbum agregado a \"{targetPlaylist.Name}\".");
                    }
                };
                menu.Items.Add(plItem);
            }
        }

        btn.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnMoreOptionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn || Album == null) return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        var refreshItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Volver a consultar Last.fm / APIs",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSync24 }
        };
        refreshItem.Click += async (_, _) =>
        {
            await RefreshAlbumInfoAsync();
        };
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Separator());

        var artistItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = $"Ir al artista ({Album.Artist})",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Person24 }
        };
        artistItem.Click += (_, _) =>
        {
            if (OpenArtistByNameCommand?.CanExecute(Album.Artist) == true)
            {
                OpenArtistByNameCommand.Execute(Album.Artist);
            }
            else
            {
                MainViewModel.Instance?.NavigateToArtistCommand.Execute(Album.Artist);
            }
        };
        menu.Items.Add(artistItem);

        var folderItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "Mostrar en el explorador de archivos",
            Icon = new SymbolIcon { Symbol = SymbolRegular.FolderOpen24 }
        };
        folderItem.Click += (_, _) =>
        {
            var first = Album.Tracks.FirstOrDefault();
            if (first != null)
            {
                MainViewModel.Instance?.OpenTrackInExplorerCommand.Execute(first);
            }
        };
        menu.Items.Add(folderItem);

        btn.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task RefreshAlbumInfoAsync()
    {
        if (Album == null) return;
        try
        {
            IsLoading = true;
            Album.IsFetchingMetadata = true;

            var firstTrack = Album.Tracks.FirstOrDefault();
            if (firstTrack != null)
            {
                // Si es de osu!, verificar fondo en .osu y consultar/actualizar Last.fm
                if (firstTrack.Genre == "osu!" || (!string.IsNullOrEmpty(firstTrack.FilePath) && firstTrack.FilePath.IndexOf("Songs", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    string? folder = System.IO.Path.GetDirectoryName(firstTrack.FilePath);
                    if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
                    {
                        string? realBg = Services.Library.OsuLibraryService.ExtractBackgroundFromOsuFolder(folder);
                        if (!string.IsNullOrEmpty(realBg))
                        {
                            firstTrack.ArtworkPath = realBg;
                            Album.ArtworkPath = realBg;
                        }
                    }

                    string primaryArt = !string.IsNullOrWhiteSpace(firstTrack.OsuArtistRoumaji) ? firstTrack.OsuArtistRoumaji : firstTrack.Artist;
                    string primaryTit = !string.IsNullOrWhiteSpace(firstTrack.OsuTitleRoumaji) ? firstTrack.OsuTitleRoumaji : firstTrack.Title;
                    string? altArt = !string.IsNullOrWhiteSpace(firstTrack.OsuArtistUnicode) ? firstTrack.OsuArtistUnicode : null;
                    string? altTit = !string.IsNullOrWhiteSpace(firstTrack.OsuTitleUnicode) ? firstTrack.OsuTitleUnicode : null;

                    var info = await Services.Online.LastFmService.Instance.FetchTrackAlbumInfoAsync(primaryArt, primaryTit, altArt, altTit);
                    if (!string.IsNullOrWhiteSpace(info.AlbumTitle))
                    {
                        firstTrack.Album = info.AlbumTitle;
                        Album.Name = info.AlbumTitle;

                        if (!string.IsNullOrWhiteSpace(info.AlbumCoverUrl))
                        {
                            string? cachedCover = await Services.Online.LastFmService.Instance.DownloadAndCacheAlbumCoverAsync(firstTrack.Artist, info.AlbumTitle, info.AlbumCoverUrl);
                            if (!string.IsNullOrEmpty(cachedCover))
                            {
                                firstTrack.ArtworkPath = cachedCover;
                                Album.ArtworkPath = cachedCover;
                            }
                        }
                    }
                }
                else
                {
                    // Álbum de la biblioteca musical local: volver a consultar Last.fm / APIs
                    string artName = !string.IsNullOrWhiteSpace(Album.Artist) ? Album.Artist : firstTrack.Artist;
                    string albName = !string.IsNullOrWhiteSpace(Album.Name) ? Album.Name : (firstTrack.Album ?? "");
                    var info = await Services.Online.LastFmService.Instance.FetchTrackAlbumInfoAsync(artName, firstTrack.Title);
                    if (!string.IsNullOrWhiteSpace(info.AlbumCoverUrl))
                    {
                        string? cachedCover = await Services.Online.LastFmService.Instance.DownloadAndCacheAlbumCoverAsync(artName, albName, info.AlbumCoverUrl);
                        if (!string.IsNullOrEmpty(cachedCover))
                        {
                            Album.ArtworkPath = cachedCover;
                            foreach (var t in Album.Tracks)
                            {
                                if (string.IsNullOrEmpty(t.ArtworkPath) || t.ArtworkPath == firstTrack.ArtworkPath)
                                    t.ArtworkPath = cachedCover;
                            }
                        }
                    }
                }

                // Recargar imagen de portada y color dominante
                Album.CoverImage = null;
                Album.DominantHeaderBrush = null;
                var cover = Album.CoverImageSource;
                if (cover != null)
                {
                    Album.DominantHeaderBrush = Services.ThemeService.CreateAccentedBoxBrush(cover);
                }
            }

            Album.RefreshStats();
            Favoritos.Instance.SyncTracksFavoriteState(Album.Tracks);
            PlayEntranceAnimation();

            MainViewModel.Instance?.ShowToast("Álbum", "Información del álbum actualizada.", false, 2500);
        }
        catch (Exception ex)
        {
            MainViewModel.Instance?.ShowToast("Error", $"No se pudo actualizar: {ex.Message}", true, 3000);
        }
        finally
        {
            if (Album != null) Album.IsFetchingMetadata = false;
            IsLoading = false;
        }
    }
}
