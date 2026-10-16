using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using BenyComes.Data;
using BenyComes.Services;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BenyComes.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly LibraryDao _libraryDao = LibraryDao.Instance;
    private readonly ThemeService _theme = ThemeService.Instance;

    public ObservableCollection<string> LibraryFolders { get; } = new();

    public List<(string Name, Color Color)> PresetAccents => ThemeService.PresetAccents;

    [ObservableProperty]
    private string _selectedTheme = "Oscuro";

    [ObservableProperty]
    private bool _autoExtractAccent;

    [ObservableProperty]
    private Color _currentAccentColor;

    [ObservableProperty]
    private bool _isAutoplayOnStartup;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanningStatus = "Listo";

    [ObservableProperty]
    private double _nowPlayingBackgroundDarkness = 0.70;

    [ObservableProperty]
    private string _selectedVisualizerMode = "Barras de Espectro";

    public string AppVersion => "v1.0.0 (Release Final) - Build x64 Native (.NET 8.0)";
    public string AudioEngineInfo => "NAudio CoreAudioApi (WASAPI Shared / Exclusive Bit-Perfect low-latency event-sync)";
    public string TechnologiesInfo => ".NET 8.0 • WPF-UI (Fluent Design 2) • TagLibSharp • Microsoft.Data.Sqlite (WAL)";

    public event Action? OnLibraryUpdated;
    public event Func<IEnumerable<string>, Task>? OnScanRequested;

    public SettingsViewModel()
    {
        _selectedTheme = _theme.CurrentTheme;
        _autoExtractAccent = _theme.AutoExtractAccent;
        _currentAccentColor = _theme.CurrentAccentColor;

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        await LoadFoldersAsync();
        string autoVal = await _libraryDao.GetSettingAsync("autoplay_startup", "false");
        bool isEnabled = bool.TryParse(autoVal, out var b) && b;
        IsAutoplayOnStartup = isEnabled;
        AudioPlayerService.Instance.IsAutoplayOnStartup = isEnabled;

        string darknessVal = await _libraryDao.GetSettingAsync("now_playing_darkness", "0.70");
        if (double.TryParse(darknessVal, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            NowPlayingBackgroundDarkness = Math.Clamp(d, 0.0, 1.0);
            AudioVisualizerService.Instance.BackgroundDarkness = NowPlayingBackgroundDarkness;
        }

        string visMode = await _libraryDao.GetSettingAsync("visualizer_mode", "Barras de Espectro");
        SelectedVisualizerMode = visMode;
        AudioVisualizerService.Instance.SelectedVisualizerMode = visMode;
    }

    partial void OnIsAutoplayOnStartupChanged(bool value)
    {
        AudioPlayerService.Instance.IsAutoplayOnStartup = value;
        _ = _libraryDao.SetSettingAsync("autoplay_startup", value.ToString());
    }

    partial void OnNowPlayingBackgroundDarknessChanged(double value)
    {
        AudioVisualizerService.Instance.BackgroundDarkness = value;
        _ = _libraryDao.SetSettingAsync("now_playing_darkness", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnSelectedVisualizerModeChanged(string value)
    {
        AudioVisualizerService.Instance.SelectedVisualizerMode = value;
        _ = _libraryDao.SetSettingAsync("visualizer_mode", value);
    }

    [RelayCommand]
    public async Task LoadFoldersAsync()
    {
        LibraryFolders.Clear();
        var list = await _libraryDao.GetLibraryFoldersAsync();
        foreach (var f in list)
        {
            LibraryFolders.Add(f);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar Carpeta de Música"
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName))
        {
            await _libraryDao.AddLibraryFolderAsync(dialog.FolderName);
            if (!LibraryFolders.Contains(dialog.FolderName))
            {
                LibraryFolders.Add(dialog.FolderName);
            }
            if (OnScanRequested != null)
            {
                await OnScanRequested.Invoke(new[] { dialog.FolderName });
            }
            OnLibraryUpdated?.Invoke();
        }
    }

    [RelayCommand]
    public async Task RemoveFolderAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        var result = System.Windows.MessageBox.Show(
            $"¿Estás seguro de que deseas quitar la carpeta \"{folderPath}\" y eliminar todas sus pistas asociadas de la base de datos?",
            "Quitar Carpeta de Biblioteca",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question
        );
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _libraryDao.RemoveLibraryFolderAndTracksAsync(folderPath);
            LibraryFolders.Remove(folderPath);
            await LibraryCacheService.Instance.ReloadFromDatabaseAsync();
            OnLibraryUpdated?.Invoke();
        }
    }

    [RelayCommand]
    private async Task RescanAllAsync()
    {
        if (OnScanRequested != null)
        {
            await OnScanRequested.Invoke(LibraryFolders.ToList());
        }
        await LibraryCacheService.Instance.ReloadFromDatabaseAsync();
        OnLibraryUpdated?.Invoke();
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        IsScanning = true;
        ScanningStatus = $"Escaneando {Path.GetFileName(folderPath)}...";

        await Task.Run(async () =>
        {
            string[] extensions = { ".flac", ".ogg", ".wav", ".mp3", ".m4a", ".aac", ".opus", ".wma" };
            var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            int count = 0;
            foreach (var file in files)
            {
                count++;
                var track = await MetadataService.Instance.LoadTrackMetadataAsync(file);
                await _libraryDao.InsertOrUpdateTrackAsync(track);
            }
        });

        ScanningStatus = $"Escaneo completado en {Path.GetFileName(folderPath)}.";
        IsScanning = false;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _theme.SetTheme(value);
    }

    partial void OnAutoExtractAccentChanged(bool value)
    {
        _theme.AutoExtractAccent = value;
    }

    [RelayCommand]
    private void SelectAccent(Color color)
    {
        CurrentAccentColor = color;
        _theme.SetAccentColor(color);
        AutoExtractAccent = false;
    }

    // Smart Artist Splitter & Exceptions
    public ObservableCollection<string> ArtistSeparators => ArtistSplitterService.Instance.Separators;
    public ObservableCollection<string> ArtistExceptions => ArtistSplitterService.Instance.Exceptions;

    public ObservableCollection<string> FilteredSeparators { get; } = new();
    public ObservableCollection<string> FilteredExceptions { get; } = new();

    [ObservableProperty]
    private bool _isSeparatorsModalOpen;

    [ObservableProperty]
    private bool _isExceptionsModalOpen;

    [ObservableProperty]
    private bool _isLibraryFoldersModalOpen;

    [ObservableProperty]
    private string _separatorSearchText = string.Empty;

    [ObservableProperty]
    private string _exceptionSearchText = string.Empty;

    partial void OnSeparatorSearchTextChanged(string value) => RefreshFilteredSeparators();
    partial void OnExceptionSearchTextChanged(string value) => RefreshFilteredExceptions();

    public void RefreshFilteredSeparators()
    {
        FilteredSeparators.Clear();
        var q = SeparatorSearchText?.Trim() ?? "";
        var items = string.IsNullOrEmpty(q)
            ? ArtistSeparators
            : ArtistSeparators.Where(s => s.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
        {
            FilteredSeparators.Add(item);
        }
    }

    public void RefreshFilteredExceptions()
    {
        FilteredExceptions.Clear();
        var q = ExceptionSearchText?.Trim() ?? "";
        var items = string.IsNullOrEmpty(q)
            ? ArtistExceptions.OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            : ArtistExceptions.Where(e => e.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            FilteredExceptions.Add(item);
        }
    }

    [RelayCommand]
    public void OpenSeparatorsModal()
    {
        SeparatorSearchText = string.Empty;
        RefreshFilteredSeparators();
        IsSeparatorsModalOpen = true;
    }

    [RelayCommand]
    public void CloseSeparatorsModal() => IsSeparatorsModalOpen = false;

    [RelayCommand]
    public void OpenExceptionsModal()
    {
        ExceptionSearchText = string.Empty;
        RefreshFilteredExceptions();
        IsExceptionsModalOpen = true;
    }

    [RelayCommand]
    public void CloseExceptionsModal() => IsExceptionsModalOpen = false;

    [RelayCommand]
    public void OpenLibraryFoldersModal() => IsLibraryFoldersModalOpen = true;

    [RelayCommand]
    public void CloseLibraryFoldersModal() => IsLibraryFoldersModalOpen = false;

    [ObservableProperty]
    private string _newSeparatorText = string.Empty;

    [ObservableProperty]
    private string _newExceptionText = string.Empty;

    [RelayCommand]
    public async Task AddSeparatorAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSeparatorText)) return;
        string trimmed = NewSeparatorText.Trim();
        if (!ArtistSeparators.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            ArtistSeparators.Add(trimmed);
            await ArtistSplitterService.Instance.SaveSettingsAsync();
            NewSeparatorText = string.Empty;
            RefreshFilteredSeparators();
        }
    }

    [RelayCommand]
    public async Task RemoveSeparatorAsync(string? sep)
    {
        if (string.IsNullOrWhiteSpace(sep)) return;
        ArtistSeparators.Remove(sep);
        await ArtistSplitterService.Instance.SaveSettingsAsync();
        RefreshFilteredSeparators();
    }

    [RelayCommand]
    public async Task AddExceptionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewExceptionText)) return;
        string trimmed = NewExceptionText.Trim();
        if (!ArtistExceptions.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            ArtistExceptions.Add(trimmed);
            await ArtistSplitterService.Instance.SaveSettingsAsync();
            NewExceptionText = string.Empty;
            RefreshFilteredExceptions();
        }
    }

    [RelayCommand]
    public async Task RemoveExceptionAsync(string? exc)
    {
        if (string.IsNullOrWhiteSpace(exc)) return;
        ArtistExceptions.Remove(exc);
        await ArtistSplitterService.Instance.SaveSettingsAsync();
        RefreshFilteredExceptions();
    }

    [ObservableProperty]
    private bool _isReindexingArtists;

    [ObservableProperty]
    private string _reindexStatusText = string.Empty;

    [RelayCommand]
    public async Task ResetArtistConfigAsync()
    {
        ArtistSplitterService.Instance.ResetToDefaults();
        await ArtistSplitterService.Instance.SaveSettingsAsync();
        ReindexStatusText = "Valores por defecto restaurados. Presiona 'Guardar y re-indexar biblioteca' para aplicar.";
    }

    [RelayCommand]
    public async Task ReindexArtistsAsync()
    {
        if (IsReindexingArtists) return;
        IsReindexingArtists = true;
        ReindexStatusText = "Guardando reglas y re-indexando biblioteca en segundo plano...";
        try
        {
            await ArtistSplitterService.Instance.SaveSettingsAsync();
            await Task.Run(async () =>
            {
                await LibraryCacheService.Instance.ReloadFromDatabaseAsync();
            });
            OnLibraryUpdated?.Invoke();
            ReindexStatusText = $"¡Biblioteca re-indexada con éxito! ({LibraryCacheService.Instance.CachedArtists.Count} artistas detectados)";
        }
        finally
        {
            IsReindexingArtists = false;
        }
    }
}
