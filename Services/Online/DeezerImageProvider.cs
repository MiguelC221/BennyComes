using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BenyComes.Services.Online;

/// <summary>
/// Fetches official high-resolution artist images from Deezer's public API.
/// Validates artist names with diacritic and punctuation normalization to prevent mismatches.
/// </summary>
public class DeezerImageProvider : IArtistImageProvider
{
    public string ProviderName => "Deezer";
    private readonly HttpClient _http;

    public DeezerImageProvider(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<string>> GetArtistImageUrlsAsync(string artistName, CancellationToken ct = default)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(artistName)) return urls;

        try
        {
            string url = $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(artistName.Trim())}&limit=5";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return urls;

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.GetArrayLength() == 0)
                return urls;

            string normalizedTarget = NormalizeName(artistName);

            foreach (var item in dataArr.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameProp)) continue;
                string returnedName = nameProp.GetString() ?? "";
                string normalizedReturned = NormalizeName(returnedName);

                if (AreNamesMatching(normalizedTarget, normalizedReturned))
                {
                    if (item.TryGetProperty("picture_xl", out var pXl) && !string.IsNullOrWhiteSpace(pXl.GetString()))
                    {
                        urls.Add(pXl.GetString()!);
                    }
                    else if (item.TryGetProperty("picture_big", out var pBig) && !string.IsNullOrWhiteSpace(pBig.GetString()))
                    {
                        urls.Add(pBig.GetString()!);
                    }

                    // Complement with top album covers for this artist to provide gallery variety
                    if (item.TryGetProperty("id", out var idProp) && !ct.IsCancellationRequested)
                    {
                        long artistId = idProp.GetInt64();
                        try
                        {
                            string albumsUrl = $"https://api.deezer.com/artist/{artistId}/albums?limit=6";
                            using var albResp = await _http.GetAsync(albumsUrl, ct).ConfigureAwait(false);
                            if (albResp.IsSuccessStatusCode)
                            {
                                string albJson = await albResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                using var albDoc = JsonDocument.Parse(albJson);
                                if (albDoc.RootElement.TryGetProperty("data", out var albArr))
                                {
                                    foreach (var alb in albArr.EnumerateArray())
                                    {
                                        if (alb.TryGetProperty("cover_xl", out var cXl) && !string.IsNullOrWhiteSpace(cXl.GetString()))
                                            urls.Add(cXl.GetString()!);
                                        else if (alb.TryGetProperty("cover_big", out var cBig) && !string.IsNullOrWhiteSpace(cBig.GetString()))
                                            urls.Add(cBig.GetString()!);

                                        if (urls.Count >= 8) break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (urls.Count > 0) break; // Found exact matching artist
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeezerImageProvider] Error querying artist image for '{artistName}': {ex.Message}");
        }

        return urls;
    }

    public static string NormalizeName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string formD = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (char c in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
        }
        return sb.ToString().Trim();
    }

    public static bool AreNamesMatching(string target, string candidate)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(candidate)) return false;
        if (target == candidate) return true;

        // Ignore leading "the" (e.g. "The Beatles" vs "Beatles")
        string stripThe(string s) => s.StartsWith("the", StringComparison.Ordinal) && s.Length > 3 ? s.Substring(3) : s;
        return stripThe(target) == stripThe(candidate);
    }
}
