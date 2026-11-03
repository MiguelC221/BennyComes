using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BenyComes.Services.Online;

/// <summary>
/// Fetches high-resolution artist images directly from Last.fm's public artist gallery (+images).
/// Converts thumbnails to high-resolution uncropped originals (770x0 / raw).
/// Excellent fallback for Doujin, Japanese, Touhou, Vocaloid, and EDM artists (e.g., Camellia)
/// whose pictures are abundant on Last.fm but missing on Deezer and Fanart.tv.
/// </summary>
public class LastFmImageProvider : IArtistImageProvider
{
    public string ProviderName => "Last.fm";
    private readonly HttpClient _http;

    public LastFmImageProvider(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<string>> GetArtistImageUrlsAsync(string artistName, CancellationToken ct = default)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(artistName)) return urls;

        try
        {
            // Clean artist name of search-breaking suffixes (e.g. "feat. ...")
            string queryArtist = CleanArtistName(artistName);
            string url = $"https://www.last.fm/music/{Uri.EscapeDataString(queryArtist)}/+images";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9,ja;q=0.8");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // If not found and name differs from original, attempt raw query
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound && !string.Equals(queryArtist, artistName, StringComparison.OrdinalIgnoreCase))
                {
                    string fallbackUrl = $"https://www.last.fm/music/{Uri.EscapeDataString(artistName.Trim())}/+images";
                    using var fbReq = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
                    fbReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    using var fbResp = await _http.SendAsync(fbReq, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                    if (fbResp.IsSuccessStatusCode)
                    {
                        string fbHtml = await fbResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        return ExtractImageUrls(fbHtml);
                    }
                }
                return urls;
            }

            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractImageUrls(html);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LastFmImageProvider] Error fetching from Last.fm gallery: {ex.Message}");
            return urls;
        }
    }

    private static List<string> ExtractImageUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return urls;

        // Match fastly cdn image hashes on Last.fm artist images gallery
        // Patterns seen on Last.fm:
        // https://lastfm.freetls.fastly.net/i/u/avatar170s/{hash}.jpg or .png
        // https://lastfm.freetls.fastly.net/i/u/174s/{hash}.jpg
        // https://lastfm.freetls.fastly.net/i/u/300x300/{hash}.jpg
        // https://lastfm.freetls.fastly.net/i/u/ar0/{hash}.jpg
        var matches = Regex.Matches(html, @"https://lastfm\.freetls\.fastly\.net/i/u/(?:avatar170s|174s|300x300|ar0|avatar70s|64s|u)/([a-zA-Z0-9]+(?:\.[a-zA-Z]+)?)", RegexOptions.IgnoreCase);

        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            if (m.Groups.Count > 1)
            {
                string raw = m.Groups[1].Value;
                string hash = raw;
                string ext = ".jpg";
                int dotIdx = raw.LastIndexOf('.');
                if (dotIdx > 0)
                {
                    hash = raw.Substring(0, dotIdx);
                    ext = raw.Substring(dotIdx);
                }

                if (seenHashes.Add(hash))
                {
                    // 770x0 is Last.fm's high-res responsive preset; 
                    // alternatively _/hash is raw uncompressed. 770x0 works universally on all fastly servers.
                    urls.Add($"https://lastfm.freetls.fastly.net/i/u/770x0/{hash}{ext}");
                }
            }
        }

        return urls;
    }

    private static string CleanArtistName(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        string result = artist.Trim();
        // Remove trailing "feat. ..." or "ft. ..."
        int featIdx = result.IndexOf(" feat", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 0) result = result.Substring(0, featIdx).Trim();
        featIdx = result.IndexOf(" ft.", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 0) result = result.Substring(0, featIdx).Trim();
        return result;
    }
}
