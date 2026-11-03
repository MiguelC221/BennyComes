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

    public AlbumModel? Album
    {
        get => (AlbumModel?)GetValue(AlbumProperty);
        set => SetValue(AlbumProperty, value);
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

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var slideAnim = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };

        RootAlbumContentGrid.BeginAnimation(OpacityProperty, fadeAnim);
        RootAlbumSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Album == null) return;

        if (e.Delta < 0 && !IsCompactHeader)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = true;
        }
        else if (e.Delta > 0 && IsCompactHeader && _lastVerticalOffset <= 24.0)
        {
            _lastHeaderToggleTick = Environment.TickCount64;
            IsCompactHeader = false;
        }
    }

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Album == null) return;
        _lastVerticalOffset = e.VerticalOffset;

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
        NavigateToArtistFromAlbum(Album?.Artist);
        e.Handled = true;
    }

    private void OnTrackArtistButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Track track && !string.IsNullOrWhiteSpace(track.Artist))
        {
            NavigateToArtistFromAlbum(track.Artist);
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
}
