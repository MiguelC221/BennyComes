using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BenyComes.Models;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Services.Online;

public enum EnrichmentStatus
{
    Idle,
    Running,
    Paused,
    NoInternet,
    Completed
}

/// <summary>
/// Background mass artist enrichment service.
/// Enriches artists in LibraryCacheService.CachedArtists with biographies (via official Last.fm API)
/// and high-resolution portraits (via pluggable IArtistImageProvider / Deezer) without blocking UI or scanning.
/// </summary>
public partial class ArtistEnrichmentService : ObservableObject
{
    private static readonly Lazy<ArtistEnrichmentService> _lazy = new(() => new ArtistEnrichmentService());
    public static ArtistEnrichmentService Instance => _lazy.Value;

    private readonly HttpClient _http;
    private readonly LastFmService _lastFm = LastFmService.Instance;
    private IArtistImageProvider _imageProvider;

    private readonly SemaphoreSlim _workerLock = new(1, 1);
    private readonly SemaphoreSlim _imageDownloadLock = new(2, 2);
    private CancellationTokenSource? _cts;
    private Task? _runningTask;

    [ObservableProperty]
    private EnrichmentStatus _status = EnrichmentStatus.Idle;

    [ObservableProperty]
    private string _statusText = "Inactivo";

    [ObservableProperty]
    private string _currentArtistName = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _processedCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private double _progressPercentage;

    public bool IsRunning => Status == EnrichmentStatus.Running;
    public bool IsPaused => Status == EnrichmentStatus.Paused || Status == EnrichmentStatus.NoInternet;

    private ArtistEnrichmentService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        _imageProvider = new FanartTvImageProvider(_http);
    }

    /// <summary>
    /// Configure or replace the image provider (e.g. DeezerImageProvider, FanartTvImageProvider).
    /// </summary>
    public void SetImageProvider(IArtistImageProvider provider)
    {
        _imageProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Checks whether an artist has missing metadata/images on disk and is not subject to negative caching.
    /// </summary>
    public bool IsArtistPending(string artistName, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return false;

        string artistDir = _lastFm.GetArtistDirectory(artistName);

        // Negative cache check
        if (!force)
        {
            string notFoundFile = Path.Combine(artistDir, "not_found.txt");
            if (File.Exists(notFoundFile))
            {
                try
                {
                    string content = File.ReadAllText(notFoundFile).Trim();
                    if (DateTime.TryParse(content, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        if (DateTime.UtcNow - dt < TimeSpan.FromDays(30))
                        {
                            return false; // Still within 30 days negative cache
                        }
                    }
                }
                catch { }
            }
        }

        // Check if bio exists and is non-empty
        string bioSummaryFile = Path.Combine(artistDir, "bio_summary.txt");
        bool hasBio = File.Exists(bioSummaryFile) && new FileInfo(bioSummaryFile).Length > 0;

        // Check if at least one image exists
        var localImages = _lastFm.GetLocalArtistImages(artistDir, includeOmitted: true);
        bool hasImages = localImages.Count > 0;

        // Pending if either bio or photos are missing
        return !hasBio || !hasImages;
    }

    /// <summary>
    /// Collects all pending artists from LibraryCacheService and starts background processing if not already running.
    /// </summary>
    public async Task EnqueueAndStartAsync(bool force = false)
    {
        if (!_lastFm.IsEnabled || !ArtistModel.IsLastFmArtistSyncActive)
        {
            Status = EnrichmentStatus.Idle;
            StatusText = "Servicios en línea o sincronización de artistas desactivada";
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastFm.ApiKey) && string.IsNullOrWhiteSpace(_lastFm.FanartTvApiKey))
        {
            Status = EnrichmentStatus.Idle;
            StatusText = "Configura tu API Key de Last.fm o Fanart.tv en Ajustes";
            return;
        }

        var allArtists = LibraryCacheService.Instance.CachedArtists.ToList();
        if (allArtists.Count == 0) return;

        // Expand any collaborations with ArtistSplitterService to ensure purely individual artist queries
        var splitArtists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var art in allArtists)
        {
            var split = ArtistSplitterService.Instance.SplitArtists(art.Name);
            foreach (var a in split)
            {
                if (!string.IsNullOrWhiteSpace(a) && !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                {
                    splitArtists.Add(a);
                }
            }
        }

        var pendingList = splitArtists.Where(a => IsArtistPending(a, force)).ToList();
        if (pendingList.Count == 0)
        {
            Status = EnrichmentStatus.Completed;
            StatusText = "Todos los artistas cuentan con información completa";
            return;
        }

        TotalCount = pendingList.Count;
        ProcessedCount = 0;
        PendingCount = pendingList.Count;
        ProgressPercentage = 0.0;

        await StartQueueAsync(pendingList, force);
    }

    public async Task StartQueueAsync(List<string> artistsToProcess, bool force = false)
    {
        await _workerLock.WaitAsync();
        try
        {
            if (IsRunning) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Status = EnrichmentStatus.Running;
            StatusText = "En curso";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));

            _runningTask = Task.Run(() => ProcessQueueLoopAsync(artistsToProcess, force, token), token);
        }
        finally
        {
            _workerLock.Release();
        }
    }

    private async Task ProcessQueueLoopAsync(List<string> artists, bool force, CancellationToken ct)
    {
        App.LogMessage($"[ArtistEnrichment] Iniciando cola de enriquecimiento para {artists.Count} artistas pendientes.");
        int successCount = 0;
        int notFoundCount = 0;
        int errorCount = 0;

        for (int i = 0; i < artists.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            while (IsPaused && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
            }
            if (ct.IsCancellationRequested) break;

            string artistName = artists[i];
            CurrentArtistName = artistName;
            ProcessedCount = i;
            PendingCount = artists.Count - i;
            ProgressPercentage = artists.Count > 0 ? (double)i / artists.Count * 100.0 : 0.0;
            StatusText = $"Procesando {i + 1} de {artists.Count}: {artistName}";

            bool artistSuccess = false;
            bool artistNotFound = false;

            try
            {
                var result = await ProcessSingleArtistWithBackoffAsync(artistName, force, ct);
                artistSuccess = result.Success;
                artistNotFound = result.NotFound;

                if (artistSuccess) successCount++;
                if (artistNotFound) notFoundCount++;
            }
            catch (HttpRequestException ex)
            {
                errorCount++;
                System.Diagnostics.Debug.WriteLine($"[ArtistEnrichment] Network error processing {artistName}: {ex.Message}");
                // General connection lost: Pause queue rather than marking as not found!
                Status = EnrichmentStatus.NoInternet;
                StatusText = "Sin conexión a internet (Pausado automáticamente)";
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsPaused));
                App.LogMessage($"[ArtistEnrichment] Conexión a internet perdida durante '{artistName}'. Cola pausada.");

                // Wait before retrying
                await Task.Delay(5000, CancellationToken.None);
                i--; // Retry this artist when resumed
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                errorCount++;
                System.Diagnostics.Debug.WriteLine($"[ArtistEnrichment] Unexpected error on {artistName}: {ex.Message}");
            }

            // Rate limiting delay between artist queries (<= 4 requests/sec)
            try
            {
                await Task.Delay(260, ct);
            }
            catch (OperationCanceledException) { break; }
        }

        ProcessedCount = artists.Count;
        PendingCount = 0;
        ProgressPercentage = 100.0;
        CurrentArtistName = string.Empty;

        if (ct.IsCancellationRequested)
        {
            Status = EnrichmentStatus.Idle;
            StatusText = "Cancelado por el usuario";
        }
        else if (Status != EnrichmentStatus.NoInternet)
        {
            Status = EnrichmentStatus.Completed;
            StatusText = $"Completado ({successCount} actualizados, {notFoundCount} no encontrados)";
        }

        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));

        App.LogMessage($"[ArtistEnrichment] Cola finalizada. Completados: {successCount}, No encontrados: {notFoundCount}, Errores: {errorCount}.");
    }

    private async Task<(bool Success, bool NotFound)> ProcessSingleArtistWithBackoffAsync(string artistName, bool force, CancellationToken ct)
    {
        string artistDir = _lastFm.GetArtistDirectory(artistName);
        string notFoundFile = Path.Combine(artistDir, "not_found.txt");
        string bioSummaryFile = Path.Combine(artistDir, "bio_summary.txt");
        string bioFullFile = Path.Combine(artistDir, "bio_full.txt");

        bool hasBio = File.Exists(bioSummaryFile) && new FileInfo(bioSummaryFile).Length > 0;
        var localImages = _lastFm.GetLocalArtistImages(artistDir, includeOmitted: true);
        bool hasImages = localImages.Count > 0;

        bool bioUpdated = false;
        bool imageDownloaded = false;
        bool isLastFmNotFound = false;

        // 1. Fetch Biography via official Last.fm API with exponential backoff
        if (!hasBio && _lastFm.IsEnabled && !string.IsNullOrWhiteSpace(_lastFm.ApiKey))
        {
            var (summary, full, _, notFound) = await ExecuteWithBackoffAsync(
                async token => await _lastFm.FetchArtistBioViaApiAsync(artistName, token),
                ct
            );

            isLastFmNotFound = notFound;

            if (!string.IsNullOrWhiteSpace(summary))
            {
                await File.WriteAllTextAsync(bioSummaryFile, summary, ct);
                bioUpdated = true;
            }
            if (!string.IsNullOrWhiteSpace(full))
            {
                await File.WriteAllTextAsync(bioFullFile, full, ct);
                bioUpdated = true;
            }
        }

        // 2. Fetch Images via unified single-source fallback (Automático) if missing
        bool isImageNotFound = false;
        if (!hasImages)
        {
            var imageUrls = await ExecuteWithBackoffAsync(
                async token => await _lastFm.FetchImageUrlsBySourceAsync(artistName, "Automático", token),
                ct
            );

            if (imageUrls != null && imageUrls.Count > 0)
            {
                await _imageDownloadLock.WaitAsync(ct);
                try
                {
                    string targetFile = Path.Combine(artistDir, "img_00.jpg");
                    string tempFile = targetFile + ".tmp";

                    using var req = new HttpRequestMessage(HttpMethod.Get, imageUrls[0]);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        byte[] data = await resp.Content.ReadAsByteArrayAsync(ct);
                        if (data.Length > 4096)
                        {
                            await File.WriteAllBytesAsync(tempFile, data, ct);
                            if (File.Exists(targetFile)) File.Delete(targetFile);
                            File.Move(tempFile, targetFile);

                            string primaryTxt = Path.Combine(artistDir, "primary.txt");
                            if (!File.Exists(primaryTxt))
                            {
                                await File.WriteAllTextAsync(primaryTxt, targetFile, ct);
                            }

                            imageDownloaded = true;
                        }
                    }
                }
                finally
                {
                    _imageDownloadLock.Release();
                }
            }
            else
            {
                isImageNotFound = true;
            }
        }

        // 3. Negative Caching: If neither bio nor images could be found
        bool finalHasImages = localImages.Count > 0 || imageDownloaded;
        bool finalHasBio = hasBio || bioUpdated;

        if (!finalHasBio && !finalHasImages && (isLastFmNotFound || isImageNotFound))
        {
            try
            {
                await File.WriteAllTextAsync(notFoundFile, DateTime.UtcNow.ToString("O"), ct);
            }
            catch { }
            return (false, true);
        }

        // Remove negative cache if data was successfully fetched
        if (File.Exists(notFoundFile))
        {
            try { File.Delete(notFoundFile); } catch { }
        }

        // 4. Dispatch UI Notification for ArtistModel
        if (bioUpdated || imageDownloaded)
        {
            var artistModel = LibraryCacheService.Instance.CachedArtists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase));
            if (artistModel != null)
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (File.Exists(bioSummaryFile))
                    {
                        artistModel.BiographySummary = File.ReadAllText(bioSummaryFile);
                        artistModel.HasBiography = !string.IsNullOrWhiteSpace(artistModel.BiographySummary);
                    }
                    artistModel.RefreshAvatar();
                }, DispatcherPriority.Background);
            }
        }

        return (bioUpdated || imageDownloaded, false);
    }

    private static async Task<T> ExecuteWithBackoffAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        int maxRetries = 3;
        int delayMs = 1000;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries && (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int?)ex.StatusCode >= 500))
            {
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxRetries)
            {
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
        }

        return await action(ct);
    }

    public void Pause()
    {
        if (Status == EnrichmentStatus.Running)
        {
            Status = EnrichmentStatus.Paused;
            StatusText = "Pausado por el usuario";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            App.LogMessage("[ArtistEnrichment] Cola pausada por el usuario.");
        }
    }

    public void Resume()
    {
        if (Status == EnrichmentStatus.Paused || Status == EnrichmentStatus.NoInternet)
        {
            Status = EnrichmentStatus.Running;
            StatusText = "En curso";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            App.LogMessage("[ArtistEnrichment] Cola reanudada por el usuario.");
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        Status = EnrichmentStatus.Idle;
        StatusText = "Cancelado";
        CurrentArtistName = string.Empty;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        App.LogMessage("[ArtistEnrichment] Cola cancelada por el usuario.");
    }
}
