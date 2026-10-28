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
    private bool _highlightLibraryFoldersSection = false;

    public async void TriggerLibraryFolderHighlight()
    {
        HighlightLibraryFoldersSection = true;
        await Task.Delay(3500);
        HighlightLibraryFoldersSection = false;
    }

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
    private double _windowBackgroundOpacity = 1.0;

    [ObservableProperty]
    private double _sidebarOpacity = 1.0;

    [ObservableProperty]
    private string _selectedVisualizerMode = "Barras de Espectro";

    // Last.fm & Fanart.tv Online Services
    [ObservableProperty]
    private bool _isLastFmEnabled;

    [ObservableProperty]
    private string _lastFmApiKey = string.Empty;

    [ObservableProperty]
    private bool _isTestingLastFm;

    [ObservableProperty]
    private string _lastFmStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLastFmStatusSuccess;

    [ObservableProperty]
    private string _fanartTvApiKey = string.Empty;

    [ObservableProperty]
    private bool _isTestingFanartTv;

    [ObservableProperty]
    private string _fanartTvStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isFanartTvStatusSuccess;

    // Maintenance & Diagnostics (Debug Mode & Logs)
    [ObservableProperty]
    private bool _isDebugModeEnabled;

    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        Services.Diagnostics.DiagnosticLoggerService.Instance.IsDebugModeEnabled = value;
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        Services.Diagnostics.DiagnosticLoggerService.Instance.OpenLogsFolder();
    }

    // NowPlaying Personalization
    [ObservableProperty]
    private bool _useArtistBackground = true;

    [ObservableProperty]
    private double _backgroundBlurRadius = 35;

    [ObservableProperty]
    private int _artistImageIntervalSeconds = 35;

    public const string CurrentVersionTag = "1.2.006";
    public const string GitHubReleasesUrl = "https://github.com/MiguelC221/BennyComes/releases";
    public string AppVersion => "v1.2.006 (Release Final) - Build x64 Native (.NET 8.0)";
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
        string storedTheme = await _libraryDao.GetSettingAsync("app_theme", "Oscuro");
        SelectedTheme = storedTheme;

        string winBgVal = await _libraryDao.GetSettingAsync("window_bg_opacity", "1.0");
        if (double.TryParse(winBgVal, System.Globalization.CultureInfo.InvariantCulture, out var wbo))
        {
            WindowBackgroundOpacity = Math.Clamp(wbo, 0.15, 1.0);
        }

        string sideVal = await _libraryDao.GetSettingAsync("sidebar_opacity", "1.0");
        if (double.TryParse(sideVal, System.Globalization.CultureInfo.InvariantCulture, out var sbo))
        {
            SidebarOpacity = Math.Clamp(sbo, 0.15, 1.0);
        }

        _theme.SetOpacities(WindowBackgroundOpacity, SidebarOpacity);

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

        await Services.Online.LastFmService.Instance.InitializeAsync();
        IsLastFmEnabled = Services.Online.LastFmService.Instance.IsEnabled;
        LastFmApiKey = Services.Online.LastFmService.Instance.ApiKey;
        FanartTvApiKey = Services.Online.LastFmService.Instance.FanartTvApiKey;

        string syncVal = await _libraryDao.GetSettingAsync("sync_artists_lastfm", "false");
        IsLastFmArtistImageSyncEnabled = bool.TryParse(syncVal, out var asb) && asb;

        string useBg = await _libraryDao.GetSettingAsync("now_playing_use_artist_bg", "true");
        UseArtistBackground = bool.TryParse(useBg, out var ub) ? ub : true;

        string blurVal = await _libraryDao.GetSettingAsync("now_playing_blur_radius", "35");
        if (double.TryParse(blurVal, System.Globalization.CultureInfo.InvariantCulture, out var br))
            BackgroundBlurRadius = Math.Clamp(br, 0, 80);

        string intVal = await _libraryDao.GetSettingAsync("now_playing_img_interval", "35");
        if (int.TryParse(intVal, out var it))
            ArtistImageIntervalSeconds = Math.Clamp(it, 15, 90);

        IsDebugModeEnabled = Services.Diagnostics.DiagnosticLoggerService.Instance.IsDebugModeEnabled;
    }

    [ObservableProperty]
    private bool _isLastFmArtistImageSyncEnabled;

    partial void OnIsLastFmArtistImageSyncEnabledChanged(bool value)
    {
        _ = _libraryDao.SetSettingAsync("sync_artists_lastfm", value.ToString());
        if (MainViewModel.Instance?.MyMusic != null)
        {
            MainViewModel.Instance.MyMusic.IsLastFmArtistSyncEnabled = value;
        }
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
        if (MainViewModel.Instance?.NowPlaying != null)
        {
            MainViewModel.Instance.NowPlaying.BackgroundDarkness = value;
        }
    }

    partial void OnSelectedVisualizerModeChanged(string value)
    {
        AudioVisualizerService.Instance.SelectedVisualizerMode = value;
        _ = _libraryDao.SetSettingAsync("visualizer_mode", value);
    }

    partial void OnIsLastFmEnabledChanged(bool value)
    {
        Services.Online.LastFmService.Instance.IsEnabled = value;
        _ = _libraryDao.SetSettingAsync("lastfm_enabled", value.ToString());
    }

    partial void OnLastFmApiKeyChanged(string value)
    {
        Services.Online.LastFmService.Instance.ApiKey = value.Trim();
        _ = _libraryDao.SetSettingAsync("lastfm_api_key", value.Trim());
    }

    partial void OnFanartTvApiKeyChanged(string value)
    {
        Services.Online.LastFmService.Instance.FanartTvApiKey = value.Trim();
        _ = _libraryDao.SetSettingAsync("fanarttv_api_key", value.Trim());
    }

    partial void OnUseArtistBackgroundChanged(bool value)
    {
        _ = _libraryDao.SetSettingAsync("now_playing_use_artist_bg", value.ToString());
        if (MainViewModel.Instance?.NowPlaying != null)
        {
            MainViewModel.Instance.NowPlaying.UseArtistBackground = value;
        }
    }

    partial void OnBackgroundBlurRadiusChanged(double value)
    {
        _ = _libraryDao.SetSettingAsync("now_playing_blur_radius", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (MainViewModel.Instance?.NowPlaying != null)
        {
            MainViewModel.Instance.NowPlaying.BackgroundBlurRadius = value;
        }
    }

    partial void OnArtistImageIntervalSecondsChanged(int value)
    {
        _ = _libraryDao.SetSettingAsync("now_playing_img_interval", value.ToString());
        if (MainViewModel.Instance?.NowPlaying != null)
        {
            MainViewModel.Instance.NowPlaying.ArtistImageIntervalSeconds = value;
        }
    }

    [RelayCommand]
    public async Task TestAndSaveLastFmAsync()
    {
        if (string.IsNullOrWhiteSpace(LastFmApiKey))
        {
            LastFmStatusMessage = "Ingresa una clave de API de Last.fm antes de probar.";
            IsLastFmStatusSuccess = false;
            return;
        }

        IsTestingLastFm = true;
        LastFmStatusMessage = "Conectando con Last.fm y validando clave...";
        try
        {
            var (success, msg) = await Services.Online.LastFmService.Instance.TestApiKeyAsync(LastFmApiKey);
            IsLastFmStatusSuccess = success;
            LastFmStatusMessage = msg;
            if (success)
            {
                IsLastFmEnabled = true;
                await Services.Online.LastFmService.Instance.SaveSettingsAsync(true, LastFmApiKey);
            }
        }
        finally
        {
            IsTestingLastFm = false;
        }
    }

    [RelayCommand]
    public void OpenLastFmApiSignup()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.last.fm/api/account/create") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public async Task TestAndSaveFanartTvAsync()
    {
        if (string.IsNullOrWhiteSpace(FanartTvApiKey))
        {
            FanartTvStatusMessage = "Ingresa una clave de API de Fanart.tv antes de probar.";
            IsFanartTvStatusSuccess = false;
            return;
        }

        IsTestingFanartTv = true;
        FanartTvStatusMessage = "Conectando con Fanart.tv y validando clave...";
        try
        {
            var (success, msg) = await Services.Online.LastFmService.Instance.TestFanartTvApiKeyAsync(FanartTvApiKey);
            IsFanartTvStatusSuccess = success;
            FanartTvStatusMessage = msg;
            if (success)
            {
                await Services.Online.LastFmService.Instance.SaveFanartTvKeyAsync(FanartTvApiKey);
            }
        }
        finally
        {
            IsTestingFanartTv = false;
        }
    }

    [RelayCommand]
    public void OpenFanartTvApiSignup()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://fanart.tv/get-an-api-key/") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenCurrentArtistGallery()
    {
        var artist = MainViewModel.Instance?.NowPlaying?.Artist;
        if (!string.IsNullOrWhiteSpace(artist))
        {
            _ = MainViewModel.Instance?.OpenArtistGalleryAsync(artist);
        }
    }

    // Background Artist Mass Enrichment Service
    public Services.Online.ArtistEnrichmentService ArtistEnrichment => Services.Online.ArtistEnrichmentService.Instance;

    [RelayCommand]
    public async Task StartEnrichmentAsync()
    {
        await Services.Online.ArtistEnrichmentService.Instance.EnqueueAndStartAsync(force: false);
    }

    [RelayCommand]
    public void PauseEnrichment()
    {
        Services.Online.ArtistEnrichmentService.Instance.Pause();
    }

    [RelayCommand]
    public void ResumeEnrichment()
    {
        Services.Online.ArtistEnrichmentService.Instance.Resume();
    }

    [RelayCommand]
    public void CancelEnrichment()
    {
        Services.Online.ArtistEnrichmentService.Instance.Cancel();
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
        else
        {
            await LibraryCacheService.Instance.ReloadFromDatabaseAsync();
            _ = Services.Online.ArtistEnrichmentService.Instance.EnqueueAndStartAsync();
        }
        OnLibraryUpdated?.Invoke();
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        IsScanning = true;
        ScanningStatus = $"Escaneando {Path.GetFileName(folderPath)}...";

        try
        {
            if (OnScanRequested != null)
            {
                await OnScanRequested.Invoke(new[] { folderPath });
            }
        }
        finally
        {
            ScanningStatus = $"Escaneo completado en {Path.GetFileName(folderPath)}.";
            IsScanning = false;
        }
    }

    partial void OnWindowBackgroundOpacityChanged(double value)
    {
        _theme.SetOpacities(value, SidebarOpacity);
        _ = _libraryDao.SetSettingAsync("window_bg_opacity", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnSidebarOpacityChanged(double value)
    {
        _theme.SetOpacities(WindowBackgroundOpacity, value);
        _ = _libraryDao.SetSettingAsync("sidebar_opacity", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _theme.SetTheme(value);
        _ = _libraryDao.SetSettingAsync("app_theme", value);
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

    // ==============================================================
    // ACTUALIZACIONES (GITHUB RELEASES) Y LICENCIAS DE CÓDIGO ABIERTO
    // ==============================================================
    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateStatusMessage = "Versión actual: BennyComes v1.2.005 • Haz clic en 'Buscar actualizaciones' para consultar GitHub Releases.";

    [ObservableProperty]
    private string _latestReleaseTag = "v1.2.005";

    private static readonly System.Net.Http.HttpClient _updateHttpClient = CreateUpdateHttpClient();

    private static System.Net.Http.HttpClient CreateUpdateHttpClient()
    {
        var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BennyComes-MusicPlayer/1.2.005");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        IsUpdateAvailable = false;
        UpdateStatusMessage = "Consultando GitHub Releases (github.com/MiguelC221/BennyComes/releases)...";

        try
        {
            using var response = await _updateHttpClient.GetAsync("https://api.github.com/repos/MiguelC221/BennyComes/releases/latest");
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? "v1.1") : "v1.1";
                string releaseName = root.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? tagName) : tagName;

                LatestReleaseTag = tagName;
                string cleanRemote = tagName.TrimStart('v', 'V').Trim();
                if (Version.TryParse(NormalizeVersionString(cleanRemote), out var remoteVer) &&
                    Version.TryParse(NormalizeVersionString(CurrentVersionTag), out var localVer))
                {
                    if (remoteVer > localVer)
                    {
                        IsUpdateAvailable = true;
                        UpdateStatusMessage = $"¡Nueva versión disponible: {releaseName} ({tagName})! Haz clic en 'Ir a GitHub Releases' para descargarla.";
                        return;
                    }
                }
                else if (!string.Equals(cleanRemote, CurrentVersionTag, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(cleanRemote))
                {
                    IsUpdateAvailable = true;
                    UpdateStatusMessage = $"Última versión publicada en GitHub: {releaseName} ({tagName}).";
                    return;
                }

                UpdateStatusMessage = $"Estás utilizando la versión más reciente de BennyComes (v{CurrentVersionTag}).";
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                UpdateStatusMessage = $"BennyComes v{CurrentVersionTag} está actualizado (aún no hay releases adicionales publicados en el repositorio).";
            }
            else
            {
                UpdateStatusMessage = $"No se pudo verificar automáticamente (HTTP {(int)response.StatusCode}). Puedes revisar manualmente en GitHub Releases.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Error al conectar con GitHub Releases: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private static string NormalizeVersionString(string raw)
    {
        var parts = raw.Split('-', '+')[0].Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => string.Join(".", parts.Take(3))
        };
    }

    [RelayCommand]
    public void OpenGitHubReleases()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GitHubReleasesUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenLicensesModal()
    {
        MainViewModel.Instance?.OpenLicensesModal();
    }

    [RelayCommand]
    public void OpenLicenseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task EmergencyFactoryResetAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "¿Estás completamente seguro de que deseas restablecer BennyComes a su estado de fábrica?\n\n" +
            "Esta acción de emergencia borrará de forma irreversible:\n" +
            " • Toda la base de datos de música (canciones, álbumes, artistas y favoritos)\n" +
            " • La caché local de fotos y biografías de artistas (%LocalAppData%\\MusicPlayer\\ArtistCache)\n" +
            " • El historial de reproducciones y listas inteligentes\n" +
            " • Todas las configuraciones y carpetas vinculadas\n\n" +
            "La aplicación se reiniciará inmediatamente limpia desde cero.",
            "Restablecimiento de Emergencia - BennyComes",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            // 1. Detener reproducción y servicios en segundo plano
            Services.Audio.AudioPlayerService.Instance.Stop();
            Services.Online.ArtistEnrichmentService.Instance.Cancel();

            // 2. Liberar conexiones de base de datos SQLite y forzar recolección de basura
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 3. Borrar completamente la carpeta de datos de la aplicación (%LocalAppData%\BenyComes)
            // Incluye benycomes.db, IndexDB (songs_index.db, albums_index.db, artists_index.db, search_index.db), Playlists, Logs, etc.
            var benyComesDir = Path.Combine(appData, "BenyComes");
            if (Directory.Exists(benyComesDir))
            {
                DeleteDirectoryRecursiveSafe(benyComesDir);
            }

            // 4. Borrar cachés de carátulas y artistas en %LocalAppData%\MusicPlayer
            var musicPlayerDir = Path.Combine(appData, "MusicPlayer");
            if (Directory.Exists(musicPlayerDir))
            {
                DeleteDirectoryRecursiveSafe(musicPlayerDir);
            }

            // 5. Reiniciar la aplicación de forma limpia
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Ocurrió un error al intentar restablecer la aplicación: {ex.Message}",
                "Error en restablecimiento",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private static void DeleteDirectoryRecursiveSafe(string dirPath)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }

            foreach (var dir in Directory.GetDirectories(dirPath, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                try
                {
                    Directory.Delete(dir, false);
                }
                catch { }
            }

            Directory.Delete(dirPath, true);
        }
        catch { }
    }

    public List<OpenSourceLicenseItem> OpenSourceLicenses { get; } = new()
    {
        new OpenSourceLicenseItem
        {
            Name = "WPF-UI (Lepo.co)",
            Version = "4.3.0",
            LicenseType = "MIT License",
            RepositoryUrl = "https://github.com/lepoco/wpfui",
            Copyright = "Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors.",
            LicenseText = "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the MIT License conditions."
        },
        new OpenSourceLicenseItem
        {
            Name = "NAudio & NAudio.Vorbis",
            Version = "2.2.1 / 1.5.0",
            LicenseType = "MIT License / MS-PL",
            RepositoryUrl = "https://github.com/naudio/NAudio",
            Copyright = "Copyright (c) 2020 Mark Heath & Contributors.",
            LicenseText = "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, subject to the MIT License conditions."
        },
        new OpenSourceLicenseItem
        {
            Name = "CSCore - .NET Sound Library",
            Version = "1.2.1.2",
            LicenseType = "Microsoft Public License (Ms-PL)",
            RepositoryUrl = "https://github.com/filoe/cscore",
            Copyright = "Copyright (c) Florian R. (filoe).",
            LicenseText = "This license governs use of the accompanying software. If you use the software, you accept this license. Subject to the terms of this license, each contributor grants you a non-exclusive, worldwide, royalty-free copyright and patent license under the Microsoft Public License (Ms-PL)."
        },
        new OpenSourceLicenseItem
        {
            Name = "TagLibSharp (taglib-sharp)",
            Version = "2.3.0",
            LicenseType = "LGPL-2.1 License",
            RepositoryUrl = "https://github.com/mono/taglib-sharp",
            Copyright = "Copyright (c) 2006-2023 Brian Nickel, Gabriel Burt, and Mono Project Contributors.",
            LicenseText = "This library is free software; you can redistribute it and/or modify it under the terms of the GNU Lesser General Public License as published by the Free Software Foundation; either version 2.1 of the License, or (at your option) any later version."
        },
        new OpenSourceLicenseItem
        {
            Name = "CommunityToolkit.Mvvm",
            Version = "8.4.2",
            LicenseType = "MIT License",
            RepositoryUrl = "https://github.com/CommunityToolkit/dotnet",
            Copyright = "Copyright (c) .NET Foundation and Contributors.",
            LicenseText = "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, subject to the MIT License conditions."
        },
        new OpenSourceLicenseItem
        {
            Name = "Microsoft.Data.Sqlite & .NET 8 Runtime",
            Version = "10.0.13 / 8.0",
            LicenseType = "MIT License",
            RepositoryUrl = "https://github.com/dotnet/efcore",
            Copyright = "Copyright (c) Microsoft Corporation / .NET Foundation and Contributors.",
            LicenseText = "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, subject to the MIT License conditions."
        }
    };
}

public class OpenSourceLicenseItem
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string Copyright { get; set; } = string.Empty;
    public string LicenseText { get; set; } = string.Empty;
}
