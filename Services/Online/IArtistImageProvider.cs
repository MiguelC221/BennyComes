using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BenyComes.Services.Online;

/// <summary>
/// Abstraction for artist image providers (Deezer, Fanart.tv, etc.)
/// allowing extensible plug-and-play image sources without coupling to specific scraping or APIs.
/// </summary>
public interface IArtistImageProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Fetches high-resolution image URLs for the specified artist.
    /// </summary>
    Task<List<string>> GetArtistImageUrlsAsync(string artistName, CancellationToken ct = default);
}
