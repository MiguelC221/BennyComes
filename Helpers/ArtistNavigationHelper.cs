using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BenyComes.Models;
using BenyComes.Services.Library;
using BenyComes.ViewModels;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace BenyComes.Helpers;

public static class ArtistNavigationHelper
{
    /// <summary>
    /// Maneja el clic sobre el nombre o celda de un artista.
    /// Si contiene múltiples artistas separados según la configuración (p. ej. "aran, lapix"),
    /// despliega un mini-menú para seleccionar a cuál artista ir con su imagen de la biblioteca.
    /// Si solo hay un artista, navega directamente.
    /// </summary>
    public static void HandleArtistClick(FrameworkElement targetElement, string? rawArtistName)
    {
        if (string.IsNullOrWhiteSpace(rawArtistName)) return;

        var split = ArtistSplitterService.Instance.SplitArtists(rawArtistName);
        if (split.Count <= 1)
        {
            string singleName = split.Count == 1 ? split[0] : rawArtistName.Trim();
            NavigateToArtist(singleName);
            return;
        }

        ShowArtistSelectionMenu(targetElement, split);
    }

    /// <summary>
    /// Muestra un mini-menú estilizado con la lista de artistas individuales y su foto de biblioteca.
    /// La posición permite hacer dos clics continuos en el mismo sitio para entrar al primer artista.
    /// </summary>
    public static void ShowArtistSelectionMenu(FrameworkElement targetElement, List<string> artistNames)
    {
        if (targetElement == null || artistNames == null || artistNames.Count == 0) return;

        var menu = new ContextMenu
        {
            PlacementTarget = targetElement,
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = -12,
            VerticalOffset = -10,
            Background = Application.Current?.TryFindResource("SurfaceCardBrush") as Brush
                         ?? new SolidColorBrush(Color.FromArgb(240, 24, 26, 36)),
            BorderBrush = Application.Current?.TryFindResource("SurfaceCardBorderBrush") as Brush
                          ?? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };

        // Redondear bordes del menú contextual
        var style = new Style(typeof(ContextMenu), menu.Style);
        var template = new ControlTemplate(typeof(ContextMenu));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var presenterFactory = new FrameworkElementFactory(typeof(ItemsPresenter));
        borderFactory.AppendChild(presenterFactory);
        template.VisualTree = borderFactory;
        menu.Template = template;

        foreach (var name in artistNames)
        {
            var artist = ResolveArtist(name);

            var item = new MenuItem
            {
                Padding = new Thickness(8, 6, 14, 6),
                Cursor = Cursors.Hand,
                ToolTip = $"Ver discografía de {artist.Name}"
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Avatar circular 32x32
            var avatarBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var avatarImg = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(avatarImg, BitmapScalingMode.HighQuality);

            var imgSource = artist.AvatarImageSource;
            if (imgSource != null)
            {
                avatarImg.Source = imgSource;
                avatarBorder.Child = avatarImg;
            }
            else
            {
                var icon = new SymbolIcon
                {
                    Symbol = SymbolRegular.Person24,
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = icon;

                artist.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ArtistModel.AvatarImageSource) && artist.AvatarImageSource != null)
                    {
                        targetElement.Dispatcher.InvokeAsync(() =>
                        {
                            avatarImg.Source = artist.AvatarImageSource;
                            avatarBorder.Child = avatarImg;
                        });
                    }
                };
            }

            Grid.SetColumn(avatarBorder, 0);
            grid.Children.Add(avatarBorder);

            // Información de texto
            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBlock = new TextBlock
            {
                Text = artist.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            textStack.Children.Add(nameBlock);

            if (artist.TrackCount > 0)
            {
                var countBlock = new TextBlock
                {
                    Text = $"{artist.TrackCount} canciones",
                    FontSize = 11,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                countBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                textStack.Children.Add(countBlock);
            }

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            item.Header = grid;

            var chosenArtist = artist;
            item.Click += (s, e) =>
            {
                NavigateToArtistModel(chosenArtist);
            };

            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    public static ArtistModel ResolveArtist(string name)
    {
        string trimmed = name.Trim();
        var vm = MainViewModel.Instance;
        var osuService = OsuLibraryService.Instance;

        bool Matches(ArtistModel a) =>
            string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            a.AltNames.Any(alt => string.Equals(alt, trimmed, StringComparison.OrdinalIgnoreCase));

        // 1. Si estamos en OsuLibrary o el modo osu! está activo
        if (vm?.CurrentView == "OsuLibrary" || vm?.Settings.IsOsuModeEnabled == true)
        {
            var osuArt = vm?.OsuLibrary.Artists.FirstOrDefault(Matches)
                      ?? osuService.Artists.FirstOrDefault(Matches);
            if (osuArt != null) return osuArt;
        }

        // 2. Caché general / MyMusic
        var cached = LibraryCacheService.Instance.CachedArtists.FirstOrDefault(Matches);
        if (cached != null) return cached;

        // 3. Fallback a OsuLibrary
        var fallbackOsu = osuService.Artists.FirstOrDefault(Matches);
        if (fallbackOsu != null) return fallbackOsu;

        // 4. Nuevo modelo si aún no existe en colecciones
        return new ArtistModel { Name = trimmed };
    }

    public static void NavigateToArtistModel(ArtistModel artist)
    {
        if (artist == null) return;
        var vm = MainViewModel.Instance;
        if (vm == null) return;

        if (vm.CurrentView == "OsuLibrary")
        {
            vm.OsuLibrary.OpenArtistDetail(artist);
        }
        else if (vm.CurrentView == "Favorites")
        {
            vm.Favorites.OpenArtistDetail(artist);
        }
        else
        {
            vm.NavigateToArtist(artist.Name);
        }
    }

    public static void NavigateToArtist(string artistName)
    {
        var artist = ResolveArtist(artistName);
        NavigateToArtistModel(artist);
    }
}
