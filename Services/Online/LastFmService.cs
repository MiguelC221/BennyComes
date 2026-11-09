using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Models;

namespace BenyComes.Services.Online;

public class ArtistOnlineInfo
{
    public string ArtistName { get; set; } = string.Empty;
    public string BioSummary { get; set; } = string.Empty;
    public string BioFull { get; set; } = string.Empty;
    public List<string> CachedImagePaths { get; set; } = new();
    public string? PrimaryImagePath => CachedImagePaths.FirstOrDefault();
}

public class LastFmService
{
    private static readonly Lazy<LastFmService> _lazy = new(() => new LastFmService());
    public static LastFmService Instance => _lazy.Value;

    private readonly HttpClient _http;
    private readonly LibraryDao _libraryDao = LibraryDao.Instance;

    public static string ArtistCacheBaseDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer",
        "ArtistCache"
    );

    // Sin clave de API predeterminada para distribuir el instalador limpio
    public const string DEFAULT_TEST_API_KEY = "";
    private const string LEGACY_DEV_TEST_KEY = "79f9f956a9ae6b118ee4ce507d536d7f";

    public bool IsEnabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool IsScrobblingEnabled { get; set; } = false;
    public string SessionKey { get; set; } = string.Empty;
    public string SessionUsername { get; set; } = string.Empty;
    public string FanartTvApiKey { get; set; } = string.Empty;

    public bool IsScrobbleAuthenticated =>
        !string.IsNullOrWhiteSpace(SessionKey) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    private readonly FanartTvImageProvider _fanartProvider;
    private readonly DeezerImageProvider _deezerProvider;
    private readonly LastFmImageProvider _lastFmImageProvider;

    private LastFmService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "BenyComesMusicPlayer/1.1 (Windows NT; HiFi Player)");
        _fanartProvider = new FanartTvImageProvider(_http);
        _deezerProvider = new DeezerImageProvider(_http);
        _lastFmImageProvider = new LastFmImageProvider(_http);

        try
        {
            Directory.CreateDirectory(ArtistCacheBaseDirectory);
        }
        catch { }
    }

    public async Task InitializeAsync()
    {
        string enabledVal = await _libraryDao.GetSettingAsync("lastfm_enabled", "true");
        IsEnabled = bool.TryParse(enabledVal, out var b) ? b : true;

        string storedKey = await _libraryDao.GetSettingAsync("lastfm_api_key", "");
        ApiKey = string.IsNullOrWhiteSpace(storedKey) ? string.Empty : storedKey.Trim();

        string storedSecret = await _libraryDao.GetSettingAsync("lastfm_api_secret", "");
        ApiSecret = string.IsNullOrWhiteSpace(storedSecret) ? string.Empty : storedSecret.Trim();

        string scrobbleVal = await _libraryDao.GetSettingAsync("lastfm_scrobble_enabled", "false");
        IsScrobblingEnabled = bool.TryParse(scrobbleVal, out var sb) && sb;

        string storedSessionKey = await _libraryDao.GetSettingAsync("lastfm_session_key", "");
        SessionKey = string.IsNullOrWhiteSpace(storedSessionKey) ? string.Empty : storedSessionKey.Trim();

        string storedSessionUser = await _libraryDao.GetSettingAsync("lastfm_session_user", "");
        SessionUsername = string.IsNullOrWhiteSpace(storedSessionUser) ? string.Empty : storedSessionUser.Trim();

        string storedFanartKey = await _libraryDao.GetSettingAsync("fanarttv_api_key", "");
        FanartTvApiKey = string.IsNullOrWhiteSpace(storedFanartKey) ? string.Empty : storedFanartKey.Trim();
    }

    public async Task SaveSettingsAsync(bool isEnabled, string apiKey)
    {
        IsEnabled = isEnabled;
        ApiKey = apiKey?.Trim() ?? string.Empty;

        await _libraryDao.SetSettingAsync("lastfm_enabled", isEnabled.ToString());
        await _libraryDao.SetSettingAsync("lastfm_api_key", ApiKey);
    }

    public async Task SaveFanartTvKeyAsync(string apiKey)
    {
        FanartTvApiKey = apiKey?.Trim() ?? string.Empty;
        await _libraryDao.SetSettingAsync("fanarttv_api_key", FanartTvApiKey);
    }

    public static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }

    public string GetArtistDirectory(string artistName)
    {
        string folder = SanitizeFolderName(artistName);
        string path = Path.Combine(ArtistCacheBaseDirectory, folder);
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<(bool Success, string Message)> TestApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "La clave de API de Last.fm no puede estar vacía.");
        }

        try
        {
            string url = $"https://ws.audioscrobbler.com/2.0/?method=artist.getinfo&artist=Queen&api_key={Uri.EscapeDataString(apiKey.Trim())}&format=json";
            using var response = await _http.GetAsync(url);
            string json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errProp))
            {
                string msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "Error desconocido" : "Clave de API inválida";
                return (false, $"Last.fm respondió con error: {msg}");
            }

            if (root.TryGetProperty("artist", out var artistProp))
            {
                return (true, "¡Conexión con Last.fm exitosa y clave validada!");
            }

            return (false, "Respuesta inesperada de Last.fm.");
        }
        catch (Exception ex)
        {
            return (false, $"Error al conectar con Last.fm: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> TestFanartTvApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "La clave de API de Fanart.tv no puede estar vacía.");
        }

        try
        {
            // Queen MBID: 0383dadf-2a4e-4d10-a46a-e9e041da8eb3
            string url = $"https://webservice.fanart.tv/v3/music/0383dadf-2a4e-4d10-a46a-e9e041da8eb3?api_key={Uri.EscapeDataString(apiKey.Trim())}";
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Fanart.tv respondió con código HTTP {(int)response.StatusCode}. Verifica tu Personal/Project API Key.");
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("name", out _) || doc.RootElement.TryGetProperty("artistthumb", out _))
            {
                return (true, "¡Conexión con Fanart.tv exitosa y clave validada!");
            }

            return (false, "Respuesta inesperada de Fanart.tv.");
        }
        catch (Exception ex)
        {
            return (false, $"Error al conectar con Fanart.tv: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches artist image URLs strictly from ONE source if specified,
    /// or falls back sequentially in Automático mode: Fanart.tv -> Deezer -> Last.fm (+images).
    /// Sources: "Automático", "Fanart.tv", "Deezer", "Last.fm".
    /// </summary>
    public async Task<List<string>> FetchImageUrlsBySourceAsync(string artistName, string source = "Automático", System.Threading.CancellationToken ct = default)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(artistName)) return urls;

        string normalized = source?.Trim().ToLowerInvariant() ?? "automático";

        if (normalized.Contains("last.fm") || normalized.Contains("lastfm"))
        {
            // Strictly Last.fm gallery
            try
            {
                urls = await _lastFmImageProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmService] Error fetching from Last.fm: {ex.Message}");
            }
            return urls;
        }

        if (normalized.Contains("fanart"))
        {
            // Strictly Fanart.tv
            try
            {
                urls = await _fanartProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmService] Error fetching from Fanart.tv: {ex.Message}");
            }
            return urls;
        }

        if (normalized.Contains("deezer"))
        {
            // Strictly Deezer
            try
            {
                urls = await _deezerProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmService] Error fetching from Deezer: {ex.Message}");
            }
            return urls;
        }

        // Modo "Automático": extrae de las fuentes disponibles con fallback inteligente
        // 1. Intentar con Fanart.tv primero si hay clave configurada
        if (!string.IsNullOrWhiteSpace(FanartTvApiKey))
        {
            try
            {
                var fanartUrls = await _fanartProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
                if (fanartUrls != null && fanartUrls.Count > 0)
                {
                    return fanartUrls;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmService] Fanart.tv falló, intentando Deezer como alternativa: {ex.Message}");
            }
        }

        // 2. Si Fanart.tv no devolvió imágenes o falló: intentar con Deezer
        try
        {
            var deezerUrls = await _deezerProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            if (deezerUrls != null && deezerUrls.Count > 0)
            {
                return deezerUrls;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] Error en Deezer: {ex.Message}");
        }

        // 3. Si Deezer tampoco devolvió imágenes (muy común en artistas japoneses, anime, Vocaloid o doujin como Camellia):
        // Consultar la galería de imágenes de Last.fm directamente
        try
        {
            var lastFmUrls = await _lastFmImageProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            if (lastFmUrls != null && lastFmUrls.Count > 0)
            {
                return lastFmUrls;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] Error en Last.fm: {ex.Message}");
        }

        return urls;
    }

    public async Task<ArtistOnlineInfo> GetArtistInfoAsync(string artistName, bool forceRefresh = false, string source = "Automático")
    {
        var result = new ArtistOnlineInfo { ArtistName = artistName };
        if (string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        string artistDir = GetArtistDirectory(artistName);
        string bioSummaryFile = Path.Combine(artistDir, "bio_summary.txt");
        string bioFullFile = Path.Combine(artistDir, "bio_full.txt");

        // 1. Check disk cache if not forced refresh
        if (!forceRefresh && File.Exists(bioSummaryFile))
        {
            try
            {
                result.BioSummary = await File.ReadAllTextAsync(bioSummaryFile);
                if (File.Exists(bioFullFile))
                {
                    result.BioFull = await File.ReadAllTextAsync(bioFullFile);
                }
                result.CachedImagePaths = GetLocalArtistImages(artistDir);
                if (result.CachedImagePaths.Count > 0)
                {
                    return result;
                }
            }
            catch { }
        }

        // Si el interruptor global de Last.fm está desactivado, se omite cualquier petición HTTP externa
        if (!IsEnabled)
        {
            result.CachedImagePaths = GetLocalArtistImages(artistDir);
            return result;
        }

        // 2. Fetch biography via official Last.fm REST API (artist.getInfo)
        try
        {
            var (bioSum, bioFull, _, _) = await FetchArtistBioViaApiAsync(artistName);
            if (!string.IsNullOrWhiteSpace(bioSum))
                result.BioSummary = bioSum;
            if (!string.IsNullOrWhiteSpace(bioFull))
                result.BioFull = bioFull;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] Error querying Last.fm info: {ex.Message}");
        }

        // 3. Obtain high-resolution artist images strictly from ONE source if no error (or fallback in Automático)
        var imageUrls = await FetchImageUrlsBySourceAsync(artistName, source);

        // 4. Download and cache up to 12 images as img_{idx}.jpg
        int idx = 0;
        foreach (var imgUrl in imageUrls.Distinct().Take(12))
        {
            try
            {
                string ext = ".jpg";
                string targetFile = Path.Combine(artistDir, $"img_{idx:D2}{ext}");
                string tempFile = targetFile + ".tmp";

                using var imgReq = new HttpRequestMessage(HttpMethod.Get, imgUrl);
                imgReq.Headers.Add("User-Agent", "BenyComesMusicPlayer/1.1 (Windows NT; HiFi Player)");
                using var imgResp = await _http.SendAsync(imgReq, HttpCompletionOption.ResponseContentRead);
                if (imgResp.IsSuccessStatusCode)
                {
                    byte[] data = await imgResp.Content.ReadAsByteArrayAsync();
                    // Validate: must be at least 4KB and have valid image header
                    if (data.Length > 4096)
                    {
                        bool isValidImage = false;
                        try
                        {
                            using var ms = new MemoryStream(data);
                            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                                ms,
                                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                                System.Windows.Media.Imaging.BitmapCacheOption.OnDemand);
                            if (decoder.Frames.Count > 0 && decoder.Frames[0].PixelWidth > 50 && decoder.Frames[0].PixelHeight > 50)
                            {
                                isValidImage = true;
                            }
                        }
                        catch
                        {
                            isValidImage = false;
                        }

                        if (isValidImage)
                        {
                            await File.WriteAllBytesAsync(tempFile, data);
                            if (File.Exists(targetFile)) File.Delete(targetFile);
                            File.Move(tempFile, targetFile);
                            idx++;
                        }
                    }
                }
            }
            catch { }
        }

        // 6. Save biography to disk
        try
        {
            if (!string.IsNullOrWhiteSpace(result.BioSummary))
            {
                await File.WriteAllTextAsync(bioSummaryFile, result.BioSummary);
            }
            if (!string.IsNullOrWhiteSpace(result.BioFull))
            {
                await File.WriteAllTextAsync(bioFullFile, result.BioFull);
            }
        }
        catch { }

        result.CachedImagePaths = GetLocalArtistImages(artistDir, includeOmitted: false);
        return result;
    }

    /// <summary>
    /// Fetches artist biography via the official Last.fm REST API (artist.getInfo).
    /// Tries Spanish first, then falls back to English if Spanish biography is empty.
    /// </summary>
    public async Task<(string? BioSummary, string? BioFull, string? ReturnedName, bool IsNotFound)> FetchArtistBioViaApiAsync(string artistName, System.Threading.CancellationToken ct = default)
    {
        string effectiveKey = !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey.Trim() : LEGACY_DEV_TEST_KEY;
        if (!IsEnabled || string.IsNullOrWhiteSpace(effectiveKey) || string.IsNullOrWhiteSpace(artistName))
            return (null, null, null, false);

        var esResult = await QueryArtistBioForLangAsync(artistName, effectiveKey, "es", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(esResult.BioSummary) || esResult.IsNotFound)
        {
            return esResult;
        }

        return await QueryArtistBioForLangAsync(artistName, effectiveKey, null, ct).ConfigureAwait(false);
    }

    private async Task<(string? BioSummary, string? BioFull, string? ReturnedName, bool IsNotFound)> QueryArtistBioForLangAsync(
        string artistName,
        string effectiveKey,
        string? lang,
        System.Threading.CancellationToken ct)
    {
        string langParam = !string.IsNullOrWhiteSpace(lang) ? $"&lang={lang}" : "";
        string url = $"https://ws.audioscrobbler.com/2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artistName.Trim())}&api_key={Uri.EscapeDataString(effectiveKey)}&autocorrect=1{langParam}&format=json";

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, null, null, true);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, null, null, false);
        }

        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out var errProp))
        {
            int errCode = errProp.GetInt32();
            if (errCode == 6) // Artist not found
            {
                return (null, null, null, true);
            }
            return (null, null, null, false);
        }

        if (doc.RootElement.TryGetProperty("artist", out var artistElem))
        {
            string returnedName = artistElem.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? "") : "";

            string? summary = null;
            string? full = null;

            if (artistElem.TryGetProperty("bio", out var bioElem))
            {
                if (bioElem.TryGetProperty("summary", out var summaryProp))
                {
                    summary = CleanHtml(summaryProp.GetString() ?? "");
                }
                if (bioElem.TryGetProperty("content", out var contentProp))
                {
                    full = CleanHtml(contentProp.GetString() ?? "");
                }
            }

            return (summary, full, returnedName, false);
        }

        return (null, null, null, false);
    }

    public async Task<(string Summary, string Full)> FetchArtistBioOnDemandAsync(
        string artistName,
        IReadOnlyList<string>? altNames = null,
        System.Threading.CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return (string.Empty, string.Empty);

        string artistDir = GetArtistDirectory(artistName);
        string bioSummaryFile = Path.Combine(artistDir, "bio_summary.txt");
        string bioFullFile = Path.Combine(artistDir, "bio_full.txt");

        if (File.Exists(bioSummaryFile))
        {
            try
            {
                string cachedSum = (await File.ReadAllTextAsync(bioSummaryFile, ct).ConfigureAwait(false)).Trim();
                string cachedFull = File.Exists(bioFullFile)
                    ? (await File.ReadAllTextAsync(bioFullFile, ct).ConfigureAwait(false)).Trim()
                    : cachedSum;
                if (!string.IsNullOrWhiteSpace(cachedSum))
                {
                    return (cachedSum, string.IsNullOrWhiteSpace(cachedFull) ? cachedSum : cachedFull);
                }
            }
            catch { }
        }

        if (!IsEnabled) return (string.Empty, string.Empty);

        var candidates = new List<string> { artistName };
        if (altNames != null)
        {
            foreach (var alt in altNames)
            {
                if (!string.IsNullOrWhiteSpace(alt) && !candidates.Contains(alt, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(alt);
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var (bioSum, bioFull, _, _) = await FetchArtistBioViaApiAsync(candidate, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(bioSum))
                {
                    string finalFull = string.IsNullOrWhiteSpace(bioFull) ? bioSum : bioFull;
                    try
                    {
                        await File.WriteAllTextAsync(bioSummaryFile, bioSum, ct).ConfigureAwait(false);
                        await File.WriteAllTextAsync(bioFullFile, finalFull, ct).ConfigureAwait(false);
                    }
                    catch { }
                    return (bioSum, finalFull);
                }
            }
            catch { }
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// Fast lightweight fetch that downloads ONLY the first artist image via official APIs (Fanart.tv / Deezer / Last.fm).
    /// </summary>
    public async Task<string?> FetchFirstArtistThumbnailAsync(string artistName, System.Threading.CancellationToken ct = default)
    {
        if (!IsEnabled || !ArtistModel.IsLastFmArtistSyncActive || string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return null;

        return await FetchArtistThumbnailOnDemandAsync(artistName, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and caches the primary artist photo on demand (used when opening InfoArtista) using Fanart.tv, Deezer, or Last.fm,
    /// with fallback to alternative names (e.g., Unicode/Romaji).
    /// </summary>
    public async Task<string?> FetchArtistThumbnailOnDemandAsync(string artistName, IReadOnlyList<string>? altNames = null, System.Threading.CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return null;

        string artistDir = GetArtistDirectory(artistName);
        var existing = GetLocalArtistImages(artistDir, includeOmitted: false);
        if (existing.Count > 0) return existing[0];

        var urls = await FetchImageUrlsBySourceAsync(artistName, "Automático", ct).ConfigureAwait(false);
        if (urls.Count == 0 && altNames != null)
        {
            foreach (var alt in altNames)
            {
                if (string.IsNullOrWhiteSpace(alt)) continue;
                urls = await FetchImageUrlsBySourceAsync(alt, "Automático", ct).ConfigureAwait(false);
                if (urls.Count > 0) break;
            }
        }

        if (urls.Count == 0 || ct.IsCancellationRequested) return null;
        string thumbUrl = urls[0];

        try
        {
            string targetFile = Path.Combine(artistDir, "img_00.jpg");
            using var imgReq = new HttpRequestMessage(HttpMethod.Get, thumbUrl);
            imgReq.Headers.Add("User-Agent", "BenyComesMusicPlayer/1.1 (Windows NT; HiFi Player)");
            using var imgResp = await _http.SendAsync(imgReq, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (imgResp.IsSuccessStatusCode)
            {
                byte[] data = await imgResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (data.Length > 512)
                {
                    await File.WriteAllBytesAsync(targetFile, data, ct).ConfigureAwait(false);
                    return targetFile;
                }
            }
        }
        catch { }

        return null;
    }

    public HashSet<string> GetOmittedImages(string artistDir)
    {
        var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(artistDir)) return omitted;

        string omittedTxt = Path.Combine(artistDir, "omitted.txt");
        if (File.Exists(omittedTxt))
        {
            try
            {
                foreach (var line in File.ReadAllLines(omittedTxt))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        omitted.Add(Path.GetFileName(trimmed));
                    }
                }
            }
            catch { }
        }
        return omitted;
    }

    public async Task<bool> ToggleOmitArtistImageAsync(string artistName, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(imagePath)) return false;
        string artistDir = GetArtistDirectory(artistName);
        string omittedTxt = Path.Combine(artistDir, "omitted.txt");
        var omitted = GetOmittedImages(artistDir);
        string fileName = Path.GetFileName(imagePath);

        bool isNowOmitted;
        if (omitted.Contains(fileName))
        {
            omitted.Remove(fileName);
            isNowOmitted = false;
        }
        else
        {
            omitted.Add(fileName);
            isNowOmitted = true;
        }

        try
        {
            await File.WriteAllLinesAsync(omittedTxt, omitted);
        }
        catch { }

        return isNowOmitted;
    }

    public List<string> GetLocalArtistImages(string artistDir, bool includeOmitted = false)
    {
        var list = new List<string>();
        if (!Directory.Exists(artistDir)) return list;

        var omitted = includeOmitted ? null : GetOmittedImages(artistDir);

        string[] extensions = { ".jpg", ".jpeg", ".png", ".webp" };
        var files = Directory.EnumerateFiles(artistDir)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => omitted == null || !omitted.Contains(Path.GetFileName(f)))
            .OrderBy(f => f)
            .ToList();

        // Check if there is a primary image indicated in primary.txt
        string primaryTxt = Path.Combine(artistDir, "primary.txt");
        if (File.Exists(primaryTxt))
        {
            try
            {
                string primaryPath = File.ReadAllText(primaryTxt).Trim();
                if (File.Exists(primaryPath) && files.Contains(primaryPath))
                {
                    files.Remove(primaryPath);
                    files.Insert(0, primaryPath);
                }
            }
            catch { }
        }

        return files;
    }

    public async Task SetPrimaryArtistImageAsync(string artistName, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(artistName) || !File.Exists(imagePath)) return;
        string artistDir = GetArtistDirectory(artistName);
        string primaryTxt = Path.Combine(artistDir, "primary.txt");
        await File.WriteAllTextAsync(primaryTxt, imagePath);

        // Ensure primary image is not omitted
        var omitted = GetOmittedImages(artistDir);
        string fileName = Path.GetFileName(imagePath);
        if (omitted.Remove(fileName))
        {
            string omittedTxt = Path.Combine(artistDir, "omitted.txt");
            try { await File.WriteAllLinesAsync(omittedTxt, omitted); } catch { }
        }
    }

    public async Task<string?> AddCustomArtistImageAsync(string artistName, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(artistName) || !File.Exists(sourceFilePath)) return null;

        try
        {
            string artistDir = GetArtistDirectory(artistName);
            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            string targetFile = Path.Combine(artistDir, $"custom_{DateTime.UtcNow.Ticks}{ext}");

            File.Copy(sourceFilePath, targetFile, overwrite: true);
            await SetPrimaryArtistImageAsync(artistName, targetFile);
            return targetFile;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding custom artist image: {ex.Message}");
            return null;
        }
    }

    public static string CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        // Strip <a> tags and HTML formatting
        string cleaned = Regex.Replace(html, @"<[^>]+>|&nbsp;|&quot;|&amp;", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        // Remove trailing "Read more on Last.fm" if present
        int readMoreIdx = cleaned.IndexOf("Read more on Last.fm", StringComparison.OrdinalIgnoreCase);
        if (readMoreIdx > 0)
        {
            cleaned = cleaned.Substring(0, readMoreIdx).Trim();
        }
        return cleaned;
    }

    /// <summary>
    /// Fetches track metadata (specifically Album name and Cover Art) from Last.fm API track.getInfo with track.search and album.search fallbacks.
    /// Supports multiple candidate pairs (e.g. Roumaji vs Native Unicode / Japanese / Cyrillic) and split artists.
    /// </summary>
    public async Task<(string? AlbumTitle, string? AlbumCoverUrl)> FetchTrackAlbumInfoAsync(
        string artist,
        string trackTitle,
        string? alternateArtist = null,
        string? alternateTitle = null,
        System.Threading.CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(ApiKey))
            return (null, null);

        // Build list of query pairs (Artist, Title) to query in priority order:
        var pairsToTry = new List<(string Artist, string Title)>();

        void AddArtistPairs(string? art, string? tit)
        {
            if (string.IsNullOrWhiteSpace(art) || string.IsNullOrWhiteSpace(tit)) return;

            // Generate clean title candidates: original intact title is ALWAYS top priority
            var titleVariants = new List<string>();
            string cleanTit = tit.Trim();
            titleVariants.Add(cleanTit);

            // Strip out common osu! / release difficulty suffixes or metadata tags only (e.g. "TV Size", "(Short Ver.)", "feat.")
            // without corrupting stylized names like "ai[ue]on" or "#1f1e33"
            string cutVersion = Regex.Replace(cleanTit, @"\s*[\(\[]?(TV[\s\-]?Size|Short\s*Ver\.?|Cut\s*Ver\.?|Game\s*Ver\.?|Extended\s*Mix|Original\s*Mix)[\)\]]?", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(cutVersion) && !titleVariants.Contains(cutVersion, StringComparer.OrdinalIgnoreCase))
            {
                titleVariants.Add(cutVersion);
            }

            // Only strip parentheses if they contain identifiable metadata like (feat ...), (ver ...), (from ...), etc.
            int parenIdx = cleanTit.IndexOf('(');
            if (parenIdx > 0 && cleanTit.EndsWith(")"))
            {
                string baseTit = cleanTit.Substring(0, parenIdx).Trim();
                if (!string.IsNullOrWhiteSpace(baseTit) && !titleVariants.Contains(baseTit, StringComparer.OrdinalIgnoreCase))
                {
                    titleVariants.Add(baseTit);
                }
            }

            int dashIdx = cleanTit.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0)
            {
                string baseTit = cleanTit.Substring(0, dashIdx).Trim();
                if (!string.IsNullOrWhiteSpace(baseTit) && !titleVariants.Contains(baseTit, StringComparer.OrdinalIgnoreCase))
                {
                    titleVariants.Add(baseTit);
                }
            }

            // Primary split artist candidate (e.g. "Camellia" from "Camellia feat. Nanahira")
            string? primaryArtist = null;
            try
            {
                var split = Services.Library.ArtistSplitterService.Instance.SplitArtists(art);
                if (split.Count > 0 && !string.IsNullOrWhiteSpace(split[0]))
                {
                    primaryArtist = split[0].Trim();
                }
            }
            catch { }

            string fullArt = art.Trim();

            foreach (var tVariant in titleVariants)
            {
                if (!string.IsNullOrWhiteSpace(primaryArtist))
                {
                    if (!pairsToTry.Any(p => string.Equals(p.Artist, primaryArtist, StringComparison.OrdinalIgnoreCase) &&
                                             string.Equals(p.Title, tVariant, StringComparison.OrdinalIgnoreCase)))
                    {
                        pairsToTry.Add((primaryArtist, tVariant));
                    }
                }

                if (!pairsToTry.Any(p => string.Equals(p.Artist, fullArt, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(p.Title, tVariant, StringComparison.OrdinalIgnoreCase)))
                {
                    pairsToTry.Add((fullArt, tVariant));
                }
            }
        }

        // 1. Primary candidate pair (usually Roumaji or default metadata)
        AddArtistPairs(artist, trackTitle);

        // 2. Alternate candidate pair if different (e.g. Native Unicode Kanji/Kana/Cyrillic, or vice versa)
        if (!string.IsNullOrWhiteSpace(alternateArtist) || !string.IsNullOrWhiteSpace(alternateTitle))
        {
            string altArt = !string.IsNullOrWhiteSpace(alternateArtist) ? alternateArtist : artist;
            string altTit = !string.IsNullOrWhiteSpace(alternateTitle) ? alternateTitle : trackTitle;
            AddArtistPairs(altArt, altTit);
        }

        // 3. Cross candidates if one was Unicode and the other was Roumaji
        if (!string.IsNullOrWhiteSpace(alternateArtist) && !string.IsNullOrWhiteSpace(trackTitle))
        {
            AddArtistPairs(alternateArtist, trackTitle);
        }
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(alternateTitle))
        {
            AddArtistPairs(artist, alternateTitle);
        }

        // --- STEP 1: Direct track.getInfo queries for all candidate pairs ---
        foreach (var (artCandidate, titCandidate) in pairsToTry)
        {
            try
            {
                string url = $"https://ws.audioscrobbler.com/2.0/?method=track.getinfo&artist={Uri.EscapeDataString(artCandidate)}&track={Uri.EscapeDataString(titCandidate)}&api_key={Uri.EscapeDataString(ApiKey.Trim())}&autocorrect=1&format=json";

                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("track", out var trackElem) &&
                    trackElem.TryGetProperty("album", out var albumElem))
                {
                    string? albumTitle = albumElem.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    string? coverUrl = null;

                    if (albumElem.TryGetProperty("image", out var imagesElem) && imagesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var img in imagesElem.EnumerateArray())
                        {
                            string size = img.TryGetProperty("size", out var sp) ? (sp.GetString() ?? "") : "";
                            string text = img.TryGetProperty("#text", out var tp) ? (tp.GetString() ?? "") : "";
                            if (!string.IsNullOrEmpty(text))
                            {
                                coverUrl = text;
                                if (size is "extralarge" or "large")
                                    break;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(albumTitle))
                    {
                        return (albumTitle, coverUrl);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LastFmService] FetchTrackAlbumInfoAsync error: {ex.Message}");
            }
        }

        // --- STEP 2: Fuzzy fallback via track.search (handles stylized casing / titles like AI[UE]OON) ---
        foreach (var (artCandidate, titCandidate) in pairsToTry.Take(3))
        {
            try
            {
                string searchUrl = $"https://ws.audioscrobbler.com/2.0/?method=track.search&track={Uri.EscapeDataString(titCandidate)}&artist={Uri.EscapeDataString(artCandidate)}&api_key={Uri.EscapeDataString(ApiKey.Trim())}&format=json";
                using var response = await _http.GetAsync(searchUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var resElem) &&
                    resElem.TryGetProperty("trackmatches", out var tmElem) &&
                    tmElem.TryGetProperty("track", out var tListElem) &&
                    tListElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var matchedTrack in tListElem.EnumerateArray())
                    {
                        string? matchedName = matchedTrack.TryGetProperty("name", out var np) ? np.GetString() : null;
                        string? matchedArtist = matchedTrack.TryGetProperty("artist", out var ap) ? ap.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(matchedName) && !string.IsNullOrWhiteSpace(matchedArtist))
                        {
                            // If the track name returned is different in case or spelling, re-query track.getInfo with exact Last.fm name
                            if (!string.Equals(matchedName, titCandidate, StringComparison.Ordinal) ||
                                !string.Equals(matchedArtist, artCandidate, StringComparison.Ordinal))
                            {
                                string infoUrl = $"https://ws.audioscrobbler.com/2.0/?method=track.getInfo&artist={Uri.EscapeDataString(matchedArtist)}&track={Uri.EscapeDataString(matchedName)}&api_key={Uri.EscapeDataString(ApiKey.Trim())}&autocorrect=1&format=json";
                                using var infoResp = await _http.GetAsync(infoUrl, ct).ConfigureAwait(false);
                                if (infoResp.IsSuccessStatusCode)
                                {
                                    string infoJson = await infoResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                    using var infoDoc = JsonDocument.Parse(infoJson);
                                    if (infoDoc.RootElement.TryGetProperty("track", out var tSubElem) &&
                                        tSubElem.TryGetProperty("album", out var aSubElem))
                                    {
                                        string? foundTitle = aSubElem.TryGetProperty("title", out var tp) ? tp.GetString() : null;
                                        string? coverUrl = null;
                                        if (aSubElem.TryGetProperty("image", out var imgArr) && imgArr.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var img in imgArr.EnumerateArray())
                                            {
                                                string size = img.TryGetProperty("size", out var sp) ? (sp.GetString() ?? "") : "";
                                                string text = img.TryGetProperty("#text", out var tp2) ? (tp2.GetString() ?? "") : "";
                                                if (!string.IsNullOrEmpty(text))
                                                {
                                                    coverUrl = text;
                                                    if (size is "extralarge" or "large") break;
                                                }
                                            }
                                        }

                                        if (!string.IsNullOrWhiteSpace(foundTitle))
                                        {
                                            return (foundTitle, coverUrl);
                                        }
                                    }
                                }
                            }
                            break; // Check the top match first
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (null, null);
            }
            catch { }
        }

        // --- STEP 3: Fallback via album.search / album.getInfo (for singles where track title == album title, e.g. #1f1e33) ---
        foreach (var (artCandidate, titCandidate) in pairsToTry.Take(2))
        {
            try
            {
                string albInfoUrl = $"https://ws.audioscrobbler.com/2.0/?method=album.getinfo&artist={Uri.EscapeDataString(artCandidate)}&album={Uri.EscapeDataString(titCandidate)}&api_key={Uri.EscapeDataString(ApiKey.Trim())}&autocorrect=1&format=json";
                using var albResp = await _http.GetAsync(albInfoUrl, ct).ConfigureAwait(false);
                if (albResp.IsSuccessStatusCode)
                {
                    string json = await albResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("album", out var albElem))
                    {
                        string? name = albElem.TryGetProperty("name", out var np) ? np.GetString() : null;
                        string? coverUrl = null;
                        if (albElem.TryGetProperty("image", out var imgArr) && imgArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var img in imgArr.EnumerateArray())
                            {
                                string size = img.TryGetProperty("size", out var sp) ? (sp.GetString() ?? "") : "";
                                string text = img.TryGetProperty("#text", out var tp2) ? (tp2.GetString() ?? "") : "";
                                if (!string.IsNullOrEmpty(text))
                                {
                                    coverUrl = text;
                                    if (size is "extralarge" or "large" or "mega") break;
                                }
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return (name, coverUrl);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (null, null);
            }
            catch { }
        }

        return (null, null);
    }

    /// <summary>
    /// Downloads an online album cover from Last.fm, saves it to AppData/MusicPlayer/ArtworkCache, and returns the local file path.
    /// If the file is already cached on disk, returns it directly without performing network calls.
    /// </summary>
    public async Task<string?> DownloadAndCacheAlbumCoverAsync(string artist, string album, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return null;

        try
        {
            string hash = Metadata.MetadataService.GetAlbumArtHash(artist, album);
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicPlayer",
                "ArtworkCache");

            Directory.CreateDirectory(cacheDir);
            string localFilePath = Path.Combine(cacheDir, $"{hash}.jpg");

            if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 100)
            {
                return localFilePath;
            }

            using var resp = await _http.GetAsync(imageUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length < 100) return null;

            Metadata.MetadataService.SaveArtToDiskCache(artist, album, bytes);

            if (File.Exists(localFilePath))
            {
                return localFilePath;
            }

            // Fallback direct write if SaveArtToDiskCache didn't produce the file
            await File.WriteAllBytesAsync(localFilePath, bytes).ConfigureAwait(false);
            return localFilePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] DownloadAndCacheAlbumCoverAsync error: {ex.Message}");
            return null;
        }
    }

    // =========================================================================
    // LAST.FM SCROBBLING & SESSION AUTHENTICATION (API 2.0)
    // =========================================================================

    public static string ComputeApiSignature(IDictionary<string, string> parameters, string apiSecret)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kvp in parameters.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (string.Equals(kvp.Key, "format", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Key, "callback", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            sb.Append(kvp.Key);
            sb.Append(kvp.Value);
        }
        sb.Append(apiSecret);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = System.Security.Cryptography.MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<(bool Success, string Token, string Message)> RequestAuthTokenAsync(string apiKey, string apiSecret)
    {
        string key = apiKey?.Trim() ?? string.Empty;
        string secret = apiSecret?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
        {
            return (false, string.Empty, "Ingresa tanto tu API Key como tu Shared Secret de Last.fm para autorizar el Scrobbling.");
        }

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["api_key"] = key,
                ["method"] = "auth.getToken"
            };
            string sig = ComputeApiSignature(parameters, secret);
            string url = $"https://ws.audioscrobbler.com/2.0/?method=auth.getToken&api_key={Uri.EscapeDataString(key)}&api_sig={sig}&format=json";

            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _))
            {
                string errMsg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "Error de Last.fm") : "Clave o secreto inválido";
                return (false, string.Empty, $"Last.fm: {errMsg}");
            }

            if (root.TryGetProperty("token", out var tokenProp))
            {
                string token = tokenProp.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return (true, token, "Token obtenido correctamente.");
                }
            }

            return (false, string.Empty, "No se pudo obtener el token de autorización de Last.fm.");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Error al conectar con Last.fm: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Username, string SessionKey, string Message)> CompleteAuthSessionAsync(string apiKey, string apiSecret, string token)
    {
        string key = apiKey?.Trim() ?? string.Empty;
        string secret = apiSecret?.Trim() ?? string.Empty;
        string tok = token?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(tok))
        {
            return (false, string.Empty, string.Empty, "Faltan datos para completar la sesión de Last.fm.");
        }

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["api_key"] = key,
                ["method"] = "auth.getSession",
                ["token"] = tok
            };
            string sig = ComputeApiSignature(parameters, secret);
            string url = $"https://ws.audioscrobbler.com/2.0/?method=auth.getSession&api_key={Uri.EscapeDataString(key)}&token={Uri.EscapeDataString(tok)}&api_sig={sig}&format=json";

            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errProp))
            {
                int code = errProp.ValueKind == JsonValueKind.Number ? errProp.GetInt32() : 0;
                string errMsg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "Error") : "No autorizado aún";
                if (code == 14)
                {
                    return (false, string.Empty, string.Empty, "Aún no has autorizado la aplicación en la ventana del navegador.");
                }
                return (false, string.Empty, string.Empty, $"Last.fm: {errMsg}");
            }

            if (root.TryGetProperty("session", out var sessElem))
            {
                string user = sessElem.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                string sk = sessElem.TryGetProperty("key", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(sk))
                {
                    ApiKey = key;
                    ApiSecret = secret;
                    SessionKey = sk;
                    SessionUsername = user;

                    await _libraryDao.SetSettingAsync("lastfm_api_key", ApiKey).ConfigureAwait(false);
                    await _libraryDao.SetSettingAsync("lastfm_api_secret", ApiSecret).ConfigureAwait(false);
                    await _libraryDao.SetSettingAsync("lastfm_session_key", SessionKey).ConfigureAwait(false);
                    await _libraryDao.SetSettingAsync("lastfm_session_user", SessionUsername).ConfigureAwait(false);

                    return (true, user, sk, $"¡Cuenta de Last.fm (@{user}) vinculada exitosamente para Scrobbling!");
                }
            }

            return (false, string.Empty, string.Empty, "Respuesta inesperada al obtener sesión de Last.fm.");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, string.Empty, $"Error al obtener sesión de Last.fm: {ex.Message}");
        }
    }

    public async Task DisconnectScrobbleSessionAsync()
    {
        SessionKey = string.Empty;
        SessionUsername = string.Empty;
        await _libraryDao.SetSettingAsync("lastfm_session_key", string.Empty).ConfigureAwait(false);
        await _libraryDao.SetSettingAsync("lastfm_session_user", string.Empty).ConfigureAwait(false);
    }

    private static (string Artist, string Title, string Album) ExtractScrobbleMetadata(Track track)
    {
        string artist = !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist.Trim() : string.Empty;
        string title = !string.IsNullOrWhiteSpace(track.Title) ? track.Title.Trim() : string.Empty;
        string album = !string.IsNullOrWhiteSpace(track.Album) ? track.Album.Trim() : string.Empty;

        if (string.Equals(artist, "Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artist, "Artista desconocido", StringComparison.OrdinalIgnoreCase))
        {
            artist = string.Empty;
        }

        if (string.Equals(album, "Unknown Album", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(album, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(album, "Single", StringComparison.OrdinalIgnoreCase))
        {
            album = string.Empty;
        }

        return (artist, title, album);
    }

    public async Task<bool> UpdateNowPlayingAsync(Track? track)
    {
        if (track == null || !IsEnabled || !IsScrobblingEnabled || !IsScrobbleAuthenticated)
            return false;

        var (artist, title, album) = ExtractScrobbleMetadata(track);
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return false;

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["api_key"] = ApiKey.Trim(),
                ["artist"] = artist,
                ["method"] = "track.updateNowPlaying",
                ["sk"] = SessionKey.Trim(),
                ["track"] = title
            };

            if (!string.IsNullOrWhiteSpace(album))
            {
                parameters["album"] = album;
            }

            int durationSecs = (int)Math.Round(track.Duration.TotalSeconds);
            if (durationSecs > 0)
            {
                parameters["duration"] = durationSecs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string sig = ComputeApiSignature(parameters, ApiSecret.Trim());
            var postData = new Dictionary<string, string>(parameters)
            {
                ["api_sig"] = sig,
                ["format"] = "json"
            };

            using var content = new FormUrlEncodedContent(postData);
            using var resp = await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", content).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] UpdateNowPlayingAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string Message)> ScrobbleTrackAsync(Track? track, long startTimestampUnix)
    {
        if (track == null || !IsEnabled || !IsScrobblingEnabled || !IsScrobbleAuthenticated)
            return (false, "Scrobbling desactivado o sin sesión activa.");

        var (artist, title, album) = ExtractScrobbleMetadata(track);
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return (false, "Metadatos de artista o título insuficientes para scrobble.");

        try
        {
            long ts = startTimestampUnix > 0 ? startTimestampUnix : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var parameters = new Dictionary<string, string>
            {
                ["api_key"] = ApiKey.Trim(),
                ["artist"] = artist,
                ["method"] = "track.scrobble",
                ["sk"] = SessionKey.Trim(),
                ["timestamp"] = ts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["track"] = title
            };

            if (!string.IsNullOrWhiteSpace(album))
            {
                parameters["album"] = album;
            }

            int durationSecs = (int)Math.Round(track.Duration.TotalSeconds);
            if (durationSecs > 0)
            {
                parameters["duration"] = durationSecs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string sig = ComputeApiSignature(parameters, ApiSecret.Trim());
            var postData = new Dictionary<string, string>(parameters)
            {
                ["api_sig"] = sig,
                ["format"] = "json"
            };

            using var content = new FormUrlEncodedContent(postData);
            using var resp = await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", content).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _))
            {
                string msg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "Error en scrobble") : "Error en scrobble";
                return (false, msg);
            }

            if (root.TryGetProperty("scrobbles", out _))
            {
                return (true, $"Scrobble enviado: {artist} - {title}");
            }

            return (false, "Respuesta inesperada de Last.fm al hacer scrobble.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmService] ScrobbleTrackAsync error: {ex.Message}");
            return (false, ex.Message);
        }
    }
}
