using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BenyComes.Models;

namespace BenyComes.Services.Metadata;

public class MetadataService
{
    private static readonly Lazy<MetadataService> _lazy = new(() => new MetadataService());
    public static MetadataService Instance => _lazy.Value;

    private static readonly string ArtworkCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer",
        "ArtworkCache"
    );

    private readonly ConcurrentDictionary<string, BitmapImage> _artCache = new();
    private readonly System.Threading.SemaphoreSlim _thumbLoadThrottle = new(4, 4);

    public bool TryGetMemoryCachedImage(Track? track, int decodeWidth, out BitmapImage? image)
    {
        image = null;
        if (track == null) return false;
        string hash = GetAlbumArtHash(track.Artist, track.Album);
        string memKey = $"{hash}_{decodeWidth}";
        return _artCache.TryGetValue(memKey, out image);
    }

    public BitmapImage GetFastPlaceholder(Track? track)
    {
        if (track == null) return ProceduralPalette[0];
        int seed = Math.Abs(((track.Title ?? "") + (track.Artist ?? "")).GetHashCode());
        return ProceduralPalette[seed % ProceduralPalette.Length];
    }

    public void QueueTrackThumbnailLoad(Track track, int decodeWidth, Action<BitmapImage?> onLoaded)
    {
        _ = Task.Run(async () =>
        {
            await _thumbLoadThrottle.WaitAsync().ConfigureAwait(false);
            try
            {
                var img = GetAlbumArtImage(track, decodeWidth);
                if (img != null)
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    {
                        _ = dispatcher.BeginInvoke(() => onLoaded(img), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch { }
            finally
            {
                _thumbLoadThrottle.Release();
            }
        });
    }

    private MetadataService()
    {
        try
        {
            Directory.CreateDirectory(ArtworkCacheDir);
        }
        catch { }
    }

    public static string GetAlbumArtHash(string? artist, string? album)
    {
        string raw = $"{artist?.Trim().ToLowerInvariant()}__{album?.Trim().ToLowerInvariant()}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public static void SaveArtToDiskCache(string? artist, string? album, byte[] data)
    {
        try
        {
            if (data == null || data.Length < 100) return;
            Directory.CreateDirectory(ArtworkCacheDir);
            string hash = GetAlbumArtHash(artist, album);
            string file = Path.Combine(ArtworkCacheDir, $"{hash}.jpg");
            if (!Data.LibraryIndexDatabase.Instance.IsArtworkCached(file))
            {
                byte[] dataToWrite = CompressToThumbnailBytes(data, decodeWidth: 160, jpegQuality: 72);

                string temp = Path.Combine(ArtworkCacheDir, $"{hash}_{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temp, dataToWrite);
                File.Move(temp, file, overwrite: true);
                Data.LibraryIndexDatabase.Instance.MarkArtworkCached(file);
            }
        }
        catch { }
    }

    private static byte[] CompressToThumbnailBytes(byte[] rawData, int decodeWidth = 160, int jpegQuality = 72)
    {
        try
        {
            byte[] fixedData = rawData;
            if (rawData.Length >= 2 && rawData[0] == 0xFF && rawData[1] == 0xD8)
            {
                if (rawData[^2] != 0xFF || rawData[^1] != 0xD9)
                {
                    fixedData = new byte[rawData.Length + 2];
                    Buffer.BlockCopy(rawData, 0, fixedData, 0, rawData.Length);
                    fixedData[^2] = 0xFF;
                    fixedData[^1] = 0xD9;
                }
            }

            using var inStream = new MemoryStream(fixedData);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = inStream;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = decodeWidth;
            bi.EndInit();
            bi.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bi));
            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            var compressed = outStream.ToArray();
            if (compressed.Length > 64)
            {
                return compressed;
            }
        }
        catch { }

        return rawData;
    }

    public void ReleaseThumbnailsFromMemory()
    {
        _artCache.Clear();
    }

    /// <summary>
    /// High-speed synchronous metadata extractor designed for parallel batch indexing.
    /// Avoids per-file SQLite queries, skips decoding embedded pictures if the album cover already exists in ArtworkCache,
    /// caches directory cover paths per folder, and releases byte[] buffers immediately after saving to disk.
    /// </summary>
    public Track ExtractTrackMetadataFast(string filePath, ConcurrentDictionary<string, string?>? folderArtPathCache = null)
    {
        var fileInfo = new FileInfo(filePath);
        var ext = fileInfo.Extension.TrimStart('.').ToUpperInvariant();
        var track = new Track
        {
            FilePath = filePath,
            FileFormat = ext,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            DateAdded = fileInfo.Exists ? fileInfo.CreationTimeUtc : DateTime.UtcNow,
            DateModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow
        };

        try
        {
            using var tagFile = TagLib.File.Create(filePath);

            track.Title = !string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                ? tagFile.Tag.Title
                : Path.GetFileNameWithoutExtension(filePath);

            track.Artist = !string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer)
                ? tagFile.Tag.FirstPerformer
                : (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstAlbumArtist) ? tagFile.Tag.FirstAlbumArtist : "Unknown Artist");

            track.Album = !string.IsNullOrWhiteSpace(tagFile.Tag.Album)
                ? tagFile.Tag.Album
                : "Single";

            track.Year = tagFile.Tag.Year;
            track.TrackNumber = tagFile.Tag.Track;
            track.Genre = tagFile.Tag.FirstGenre ?? "Music";

            if (tagFile.Properties != null)
            {
                track.Duration = tagFile.Properties.Duration;
                track.Bitrate = tagFile.Properties.AudioBitrate;
                track.SampleRate = tagFile.Properties.AudioSampleRate > 0 ? tagFile.Properties.AudioSampleRate : 44100;
                track.BitsPerSample = tagFile.Properties.BitsPerSample > 0 ? tagFile.Properties.BitsPerSample : 16;
                track.Channels = tagFile.Properties.AudioChannels > 0 ? tagFile.Properties.AudioChannels : 2;
            }

            string artHash = GetAlbumArtHash(track.Artist, track.Album);
            string expectedArtPath = Path.Combine(ArtworkCacheDir, $"{artHash}.jpg");

            if (Data.LibraryIndexDatabase.Instance.IsArtworkCached(expectedArtPath))
            {
                track.ArtworkPath = expectedArtPath;
            }
            else
            {
                byte[]? artBytes = null;
                if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    if (pic.Data?.Data != null && pic.Data.Data.Length > 0)
                    {
                        artBytes = pic.Data.Data;
                    }
                }

                if (artBytes == null || artBytes.Length == 0)
                {
                    artBytes = TryFindLocalFolderArtFast(filePath, folderArtPathCache);
                }

                if (artBytes != null && artBytes.Length > 0)
                {
                    SaveArtToDiskCache(track.Artist, track.Album, artBytes);
                    if (Data.LibraryIndexDatabase.Instance.IsArtworkCached(expectedArtPath))
                    {
                        track.ArtworkPath = expectedArtPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TagLib error reading {filePath}: {ex.Message}");
            track.Title = Path.GetFileNameWithoutExtension(filePath);
            track.Artist = "Unknown Artist";
            track.Album = "Unknown Album";
            track.Duration = TimeSpan.FromMinutes(3);
            track.SampleRate = 44100;
            track.BitsPerSample = 16;
            track.Channels = 2;
        }

        // Precompute sort and search fields on background worker thread
        track.FirstLetterGroup = Helpers.AlphaGroupHelper.GetAlphaGroupKey(track.Title);
        _ = track.SortTitle;
        _ = track.GroupRank;
        _ = track.SearchText;
        track.AlbumArtData = null;

        return track;
    }

    public async Task<Track> LoadTrackMetadataAsync(string filePath, bool forceTagLib = false)
    {
        if (!forceTagLib)
        {
            try
            {
                var cached = await Data.LibraryDao.Instance.GetTrackByPathAsync(filePath);
                if (cached != null)
                {
                    return cached;
                }
            }
            catch { }
        }

        return await Task.Run(() => ExtractTrackMetadataFast(filePath));
    }

    private static byte[]? TryFindLocalFolderArtFast(string audioFilePath, ConcurrentDictionary<string, string?>? folderArtPathCache)
    {
        try
        {
            var dir = Path.GetDirectoryName(audioFilePath);
            if (string.IsNullOrEmpty(dir)) return null;

            string? foundPath;
            if (folderArtPathCache != null)
            {
                foundPath = folderArtPathCache.GetOrAdd(dir, static d => FindFolderArtFilePath(d));
            }
            else
            {
                foundPath = FindFolderArtFilePath(dir);
            }

            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
            {
                return File.ReadAllBytes(foundPath);
            }
        }
        catch { }
        return null;
    }

    private static string? FindFolderArtFilePath(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            string[] candidates = { "cover.jpg", "folder.jpg", "front.jpg", "albumart.jpg", "cover.png", "folder.png", "front.png" };
            foreach (var name in candidates)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }

            return Directory.EnumerateFiles(dir, "*.*")
                .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryFindLocalFolderArt(string audioFilePath)
    {
        return TryFindLocalFolderArtFast(audioFilePath, null);
    }

    private static readonly (Color, Color)[] PaletteGradients = new[]
    {
        (Color.FromRgb(45, 25, 80), Color.FromRgb(240, 140, 60)),
        (Color.FromRgb(15, 32, 67), Color.FromRgb(84, 199, 182)),
        (Color.FromRgb(30, 20, 50), Color.FromRgb(220, 80, 120)),
        (Color.FromRgb(10, 40, 45), Color.FromRgb(80, 200, 150)),
        (Color.FromRgb(40, 15, 20), Color.FromRgb(230, 90, 60)),
        (Color.FromRgb(20, 25, 60), Color.FromRgb(140, 100, 240))
    };

    private static readonly Lazy<BitmapImage[]> _lazyPalette = new(() =>
    {
        return PaletteGradients.Select(p => CreateGradientBitmapImage(p.Item1, p.Item2, 160)).ToArray();
    });

    private static BitmapImage[] ProceduralPalette => _lazyPalette.Value;

    private static BitmapImage CreateGradientBitmapImage(Color c1, Color c2, int size)
    {
        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        for (int y = 0; y < size; y++)
        {
            double ty = (double)y / (size - 1);
            for (int x = 0; x < size; x++)
            {
                double tx = (double)x / (size - 1);
                double t = (tx + ty) / 2.0;

                byte r = (byte)(c1.R + t * (c2.R - c1.R));
                byte g = (byte)(c1.G + t * (c2.G - c1.G));
                byte b = (byte)(c1.B + t * (c2.B - c1.B));

                // Soft radial glow in upper-right
                double dx = tx - 0.7;
                double dy = ty - 0.3;
                double distSq = dx * dx + dy * dy;
                if (distSq < 0.25)
                {
                    double glow = (1.0 - Math.Sqrt(distSq) / 0.5) * 0.25;
                    r = (byte)Math.Min(255, r + glow * 255);
                    g = (byte)Math.Min(255, g + glow * 255);
                    b = (byte)Math.Min(255, b + glow * 255);
                }

                int idx = (y * stride) + (x * 4);
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;
            }
        }

        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Seek(0, SeekOrigin.Begin);

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = ms;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public string? EnsureAlbumArtDiskPath(Track? track)
    {
        if (track == null) return null;
        try
        {
            string hash = GetAlbumArtHash(track.Artist, track.Album);
            string diskPath = Path.Combine(ArtworkCacheDir, $"{hash}.jpg");

            if (File.Exists(diskPath))
            {
                return diskPath;
            }

            if (track.AlbumArtData != null && track.AlbumArtData.Length > 0)
            {
                SaveArtToDiskCache(track.Artist, track.Album, track.AlbumArtData);
                if (File.Exists(diskPath)) return diskPath;
            }

            if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            {
                var onDemand = ExtractArtFromFileOrFolder(track.FilePath);
                if (onDemand != null && onDemand.Length > 0)
                {
                    track.AlbumArtData = onDemand;
                    SaveArtToDiskCache(track.Artist, track.Album, onDemand);
                    if (File.Exists(diskPath)) return diskPath;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public BitmapImage GetAlbumArtImage(Track? track, int decodeWidth = 160)
    {
        if (track == null) return GetFallbackArt();

        string hash = GetAlbumArtHash(track.Artist, track.Album);
        string memKey = $"{hash}_{decodeWidth}";

        if (_artCache.TryGetValue(memKey, out var cached))
        {
            return cached;
        }

        string diskPath = Path.Combine(ArtworkCacheDir, $"{hash}.jpg");

        // Try extracting and caching on demand if not yet on disk
        if (!File.Exists(diskPath))
        {
            if (track.AlbumArtData != null && track.AlbumArtData.Length > 0)
            {
                SaveArtToDiskCache(track.Artist, track.Album, track.AlbumArtData);
                track.AlbumArtData = null;
            }
            else if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            {
                var onDemand = ExtractArtFromFileOrFolder(track.FilePath);
                if (onDemand != null && onDemand.Length > 0)
                {
                    SaveArtToDiskCache(track.Artist, track.Album, onDemand);
                }
            }
        }

        // Load compressed thumbnail from disk cache
        if (File.Exists(diskPath))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(diskPath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                if (decodeWidth > 0)
                {
                    bi.DecodePixelWidth = decodeWidth;
                }
                bi.EndInit();
                bi.Freeze();

                // Cap in-memory thumbnail dictionary so it never grows unbounded
                if (_artCache.Count > 120)
                {
                    _artCache.Clear();
                }
                _artCache[memKey] = bi;
                return bi;
            }
            catch
            {
                try { File.Delete(diskPath); } catch { }
            }
        }

        // Placeholder
        int seed = Math.Abs(((track.Title ?? "") + (track.Artist ?? "")).GetHashCode());
        var placeholder = ProceduralPalette[seed % ProceduralPalette.Length];
        return placeholder;
    }

    public static byte[]? ExtractArtFromFileOrFolder(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures != null && tagFile.Tag.Pictures.Length > 0)
            {
                var pic = tagFile.Tag.Pictures[0];
                if (pic.Data != null && pic.Data.Data != null && pic.Data.Data.Length > 0)
                {
                    return pic.Data.Data;
                }
            }
        }
        catch { }

        return TryFindLocalFolderArt(filePath);
    }

    /// <summary>
    /// Decodes high-quality artwork directly from the audio file or folder ONLY for the currently playing track.
    /// Does NOT store the high-res image in _artCache so it is immediately released from memory once the track finishes.
    /// </summary>
    public BitmapImage GetHighResAlbumArt(Track? track)
    {
        if (track == null) return GetFallbackArt();

        try
        {
            if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            {
                byte[]? rawBytes = ExtractArtFromFileOrFolder(track.FilePath);
                if (rawBytes != null && rawBytes.Length > 100)
                {
                    // Ensure compressed thumbnail also exists on disk for library views
                    SaveArtToDiskCache(track.Artist, track.Album, rawBytes);

                    using var ms = new MemoryStream(rawBytes);
                    var hiRes = new BitmapImage();
                    hiRes.BeginInit();
                    hiRes.StreamSource = ms;
                    hiRes.CacheOption = BitmapCacheOption.OnLoad;
                    hiRes.DecodePixelWidth = 640; // High quality for NowPlaying without wasting 4K uncompressed buffers
                    hiRes.EndInit();
                    hiRes.Freeze();
                    return hiRes;
                }
            }
        }
        catch { }

        return GetAlbumArtImage(track, decodeWidth: 160);
    }

    public BitmapImage GetBlurredBackgroundArt(Track? track)
    {
        return GetAlbumArtImage(track, decodeWidth: 96);
    }

    public static BitmapImage GenerateProceduralCover(string title, string artist)
    {
        int seed = Math.Abs((title + artist).GetHashCode());
        return ProceduralPalette[seed % ProceduralPalette.Length];
    }

    public BitmapImage GetFallbackArt()
    {
        return ProceduralPalette[0];
    }

    public static Color ExtractDominantAccentColor(BitmapSource? image)
    {
        if (image == null) return Color.FromRgb(0, 210, 106); // Default vibrant emerald/groove

        try
        {
            // Resize to 32x32 for high performance sampling
            var small = new TransformedBitmap(image, new ScaleTransform(32.0 / Math.Max(1, image.PixelWidth), 32.0 / Math.Max(1, image.PixelHeight)));
            var formatted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            double bestScore = -1;
            Color bestColor = Color.FromRgb(0, 210, 106);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                double max = Math.Max(r, Math.Max(g, b)) / 255.0;
                double min = Math.Min(r, Math.Min(g, b)) / 255.0;
                double delta = max - min;
                double saturation = max == 0 ? 0 : delta / max;
                double brightness = max;

                // We want vibrant colors: not too dark (>0.25), not washed out (<0.95), good saturation (>0.35)
                if (brightness > 0.25 && brightness < 0.95 && saturation > 0.35)
                {
                    double score = saturation * 2.0 + (1.0 - Math.Abs(brightness - 0.65));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestColor = Color.FromRgb(r, g, b);
                    }
                }
            }

            return bestColor;
        }
        catch
        {
            return Color.FromRgb(0, 210, 106);
        }
    }
}
