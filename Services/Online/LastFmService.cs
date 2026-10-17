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
    public string FanartTvApiKey { get; set; } = string.Empty;

    private readonly FanartTvImageProvider _imageProvider;

    private LastFmService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "BenyComesMusicPlayer/1.1 (Windows NT; HiFi Player)");
        _imageProvider = new FanartTvImageProvider(_http);

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
        if (string.Equals(storedKey?.Trim(), LEGACY_DEV_TEST_KEY, StringComparison.OrdinalIgnoreCase))
        {
            storedKey = string.Empty;
            await _libraryDao.SetSettingAsync("lastfm_api_key", string.Empty);
        }
        ApiKey = string.IsNullOrWhiteSpace(storedKey) ? string.Empty : storedKey.Trim();

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

    public async Task<ArtistOnlineInfo> GetArtistInfoAsync(string artistName, bool forceRefresh = false)
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

        // Si el interruptor global de Last.fm está desactivado, o la sincronización/búsqueda de artistas está desactivada, se omite cualquier petición HTTP externa
        if (!IsEnabled || !ArtistModel.IsLastFmArtistSyncActive)
        {
            result.CachedImagePaths = GetLocalArtistImages(artistDir);
            return result;
        }

        // 2. Fetch biography strictly via official Last.fm REST API (artist.getInfo)
        if (IsEnabled && !string.IsNullOrWhiteSpace(ApiKey))
        {
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
        }

        // 3. Obtain high-resolution artist images via official REST APIs (Fanart.tv + Deezer)
        var imageUrls = await _imageProvider.GetArtistImageUrlsAsync(artistName);

        // 4. Complement with Deezer album covers if gallery needs additional images
        if (imageUrls.Count < 8)
        {
            try
            {
                string deezerUrl = $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artistName)}&limit=3";
                using var dzResp = await _http.GetAsync(deezerUrl);
                if (dzResp.IsSuccessStatusCode)
                {
                    string dzJson = await dzResp.Content.ReadAsStringAsync();
                    using var dzDoc = JsonDocument.Parse(dzJson);
                    if (dzDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                    {
                        string normTarget = DeezerImageProvider.NormalizeName(artistName);
                        foreach (var candidate in dataArr.EnumerateArray())
                        {
                            string candName = candidate.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                            if (!DeezerImageProvider.AreNamesMatching(normTarget, DeezerImageProvider.NormalizeName(candName)))
                                continue;

                            if (candidate.TryGetProperty("id", out var idProp))
                            {
                                long artistId = idProp.GetInt64();
                                string albumsUrl = $"https://api.deezer.com/artist/{artistId}/albums?limit=8";
                                using var albResp = await _http.GetAsync(albumsUrl);
                                if (albResp.IsSuccessStatusCode)
                                {
                                    string albJson = await albResp.Content.ReadAsStringAsync();
                                    using var albDoc = JsonDocument.Parse(albJson);
                                    if (albDoc.RootElement.TryGetProperty("data", out var albArr))
                                    {
                                        foreach (var alb in albArr.EnumerateArray())
                                        {
                                            if (alb.TryGetProperty("cover_xl", out var cXl) && !string.IsNullOrWhiteSpace(cXl.GetString()))
                                                imageUrls.Add(cXl.GetString()!);
                                            else if (alb.TryGetProperty("cover_big", out var cBig) && !string.IsNullOrWhiteSpace(cBig.GetString()))
                                                imageUrls.Add(cBig.GetString()!);

                                            if (imageUrls.Count >= 12) break;
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        // 5. Download and cache up to 12 images as img_{idx}.jpg
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
    /// Fetches artist biography strictly via the official Last.fm REST API (artist.getInfo) without web scraping.
    /// Validates artist name matching to avoid saving incorrect data from autocorrect.
    /// </summary>
    public async Task<(string? BioSummary, string? BioFull, string? ReturnedName, bool IsNotFound)> FetchArtistBioViaApiAsync(string artistName, System.Threading.CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(artistName))
            return (null, null, null, false);

        string url = $"https://ws.audioscrobbler.com/2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artistName.Trim())}&api_key={Uri.EscapeDataString(ApiKey.Trim())}&autocorrect=1&format=json";

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, null, null, true);
        }

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                // Transient server/rate limit error
                throw new HttpRequestException($"Last.fm API returned HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }
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

            // Validate that autocorrect did not match a completely different artist
            if (!string.IsNullOrWhiteSpace(returnedName))
            {
                string normTarget = DeezerImageProvider.NormalizeName(artistName);
                string normReturned = DeezerImageProvider.NormalizeName(returnedName);
                if (!DeezerImageProvider.AreNamesMatching(normTarget, normReturned))
                {
                    System.Diagnostics.Debug.WriteLine($"[LastFmService] Autocorrect mismatch: queried '{artistName}' but received '{returnedName}'. Rejecting.");
                    return (null, null, returnedName, true);
                }
            }

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

    /// <summary>
    /// Fast lightweight fetch that downloads ONLY the first artist image via official APIs (Fanart.tv / Deezer)
    /// without any web scraping.
    /// </summary>
    public async Task<string?> FetchFirstArtistThumbnailAsync(string artistName, System.Threading.CancellationToken ct = default)
    {
        if (!IsEnabled || !ArtistModel.IsLastFmArtistSyncActive || string.IsNullOrWhiteSpace(artistName) || artistName.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return null;

        string artistDir = GetArtistDirectory(artistName);
        var existing = GetLocalArtistImages(artistDir, includeOmitted: false);
        if (existing.Count > 0) return existing[0];

        string? thumbUrl = null;

        // 1. Try Fanart.tv if user configured a Fanart.tv API Key
        if (!string.IsNullOrWhiteSpace(FanartTvApiKey) && !ct.IsCancellationRequested)
        {
            var urls = await _imageProvider.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            if (urls.Count > 0)
            {
                thumbUrl = urls[0];
            }
        }

        // 2. Fallback to Deezer official REST API (validated artist name match)
        if (string.IsNullOrEmpty(thumbUrl) && !ct.IsCancellationRequested)
        {
            try
            {
                string deezerUrl = $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artistName.Trim())}&limit=5";
                using var dzResp = await _http.GetAsync(deezerUrl, ct).ConfigureAwait(false);
                if (dzResp.IsSuccessStatusCode)
                {
                    string dzJson = await dzResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var dzDoc = JsonDocument.Parse(dzJson);
                    if (dzDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                    {
                        string normTarget = DeezerImageProvider.NormalizeName(artistName);
                        foreach (var item in dataArr.EnumerateArray())
                        {
                            string returnedName = item.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                            if (DeezerImageProvider.AreNamesMatching(normTarget, DeezerImageProvider.NormalizeName(returnedName)))
                            {
                                if (item.TryGetProperty("picture_medium", out var pMed) && !string.IsNullOrWhiteSpace(pMed.GetString()))
                                    thumbUrl = pMed.GetString()!;
                                else if (item.TryGetProperty("picture_big", out var pBig) && !string.IsNullOrWhiteSpace(pBig.GetString()))
                                    thumbUrl = pBig.GetString()!;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(thumbUrl) || ct.IsCancellationRequested) return null;

        // 3. Download only this 1 thumbnail file
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
}
