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
