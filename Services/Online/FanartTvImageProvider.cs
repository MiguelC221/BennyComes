using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BenyComes.Services.Online;

/// <summary>
/// Fetches high-resolution artist portraits (artistthumb) and backgrounds (artistbackground)
/// via official MusicBrainz MBID resolution + Fanart.tv v3 API, with automatic fallback to DeezerImageProvider.
/// </summary>
public class FanartTvImageProvider : IArtistImageProvider
{
    public string ProviderName => "Fanart.tv + Deezer";
    private readonly HttpClient _http;
    private readonly DeezerImageProvider _deezerFallback;

    public FanartTvImageProvider(HttpClient http)
    {
        _http = http;
        _deezerFallback = new DeezerImageProvider(http);
    }

    public async Task<List<string>> GetArtistImageUrlsAsync(string artistName, CancellationToken ct = default)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(artistName)) return urls;

        string fanartKey = LastFmService.Instance.FanartTvApiKey;
        if (!string.IsNullOrWhiteSpace(fanartKey))
        {
            try
            {
                string? mbid = await ResolveArtistMbidAsync(artistName, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(mbid) && !ct.IsCancellationRequested)
                {
                    string fanartUrl = $"https://webservice.fanart.tv/v3/music/{Uri.EscapeDataString(mbid)}?api_key={Uri.EscapeDataString(fanartKey.Trim())}";
                    using var resp = await _http.GetAsync(fanartUrl, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        // 1. High-resolution artist portraits (1000x1000)
                        if (root.TryGetProperty("artistthumb", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in thumbs.EnumerateArray())
                            {
                                if (item.TryGetProperty("url", out var uProp) && !string.IsNullOrWhiteSpace(uProp.GetString()))
                                {
                                    urls.Add(uProp.GetString()!);
                                    if (urls.Count >= 6) break;
                                }
                            }
                        }

                        // 2. High-resolution artist backgrounds (1920x1080)
                        if (root.TryGetProperty("artistbackground", out var bgs) && bgs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in bgs.EnumerateArray())
                            {
                                if (item.TryGetProperty("url", out var uProp) && !string.IsNullOrWhiteSpace(uProp.GetString()))
                                {
                                    urls.Add(uProp.GetString()!);
                                    if (urls.Count >= 12) break;
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FanartTvImageProvider] Error querying Fanart.tv for '{artistName}': {ex.Message}");
            }
        }

        // Complement or fallback with Deezer API if fewer than 4 images were found
        if (urls.Count < 4 && !ct.IsCancellationRequested)
        {
            var deezerUrls = await _deezerFallback.GetArtistImageUrlsAsync(artistName, ct).ConfigureAwait(false);
            foreach (var dUrl in deezerUrls)
            {
                if (!urls.Contains(dUrl))
                {
                    urls.Add(dUrl);
                }
            }
        }

        return urls;
    }

    public async Task<string?> ResolveArtistMbidAsync(string artistName, CancellationToken ct = default)
    {
        try
        {
            // 1. Try Last.fm artist.getinfo MBID first if Last.fm API key is available
            string lfmKey = LastFmService.Instance.ApiKey;
            if (!string.IsNullOrWhiteSpace(lfmKey))
            {
                string lfmUrl = $"https://ws.audioscrobbler.com/2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artistName.Trim())}&api_key={Uri.EscapeDataString(lfmKey.Trim())}&autocorrect=1&format=json";
                using var lfmResp = await _http.GetAsync(lfmUrl, ct).ConfigureAwait(false);
                if (lfmResp.IsSuccessStatusCode)
                {
                    string lfmJson = await lfmResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(lfmJson);
                    if (doc.RootElement.TryGetProperty("artist", out var artistElem))
                    {
                        string returnedName = artistElem.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                        if (DeezerImageProvider.AreNamesMatching(
                            DeezerImageProvider.NormalizeName(artistName),
                            DeezerImageProvider.NormalizeName(returnedName)))
                        {
                            if (artistElem.TryGetProperty("mbid", out var mbidProp) && !string.IsNullOrWhiteSpace(mbidProp.GetString()))
                            {
                                return mbidProp.GetString();
                            }
                        }
                    }
                }
            }

            // 2. Query official MusicBrainz WS/2 API
            string mbUrl = $"https://musicbrainz.org/ws/2/artist/?query=artist:{Uri.EscapeDataString(artistName.Trim())}&fmt=json&limit=3";
            using var req = new HttpRequestMessage(HttpMethod.Get, mbUrl);
            req.Headers.Add("User-Agent", "BenyComesMusicPlayer/1.1 (https://github.com/MiguelC221/BennyComes)");
            using var mbResp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (mbResp.IsSuccessStatusCode)
            {
                string mbJson = await mbResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var mbDoc = JsonDocument.Parse(mbJson);
                if (mbDoc.RootElement.TryGetProperty("artists", out var artistsArr) && artistsArr.ValueKind == JsonValueKind.Array)
                {
                    string normTarget = DeezerImageProvider.NormalizeName(artistName);
                    foreach (var item in artistsArr.EnumerateArray())
                    {
                        string candidateName = item.TryGetProperty("name", out var cProp) ? (cProp.GetString() ?? "") : "";
                        if (DeezerImageProvider.AreNamesMatching(normTarget, DeezerImageProvider.NormalizeName(candidateName)))
                        {
                            if (item.TryGetProperty("id", out var idProp) && !string.IsNullOrWhiteSpace(idProp.GetString()))
                            {
                                return idProp.GetString();
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        return null;
    }
}
