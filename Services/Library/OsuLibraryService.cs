using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenyComes.Models;

namespace BenyComes.Services.Library;

public class OsuBeatmapRaw
{
    public string Artist { get; set; } = string.Empty;
    public string ArtistUnicode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TitleUnicode { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string AudioFilename { get; set; } = string.Empty;
    public string OsuFilename { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public int TotalTimeMs { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int BeatmapSetId { get; set; }
}

public class OsuLibraryService
{
    private static readonly Lazy<OsuLibraryService> _instance = new(() => new OsuLibraryService());
    public static OsuLibraryService Instance => _instance.Value;

    private readonly List<Track> _osuTracks = new();
    private readonly List<ArtistModel> _osuArtists = new();

    public IReadOnlyList<Track> Tracks => _osuTracks;
    public IReadOnlyList<ArtistModel> Artists => _osuArtists;

    public bool IsLoaded { get; private set; }
    public bool PreferUnicodeMetadata { get; set; } = false;

    public event Action? LibraryUpdated;

    private OsuLibraryService() { }

    public static string DetectOsuDirectory()
    {
        // 1. Common default locations
        string[] candidates =
        {
            @"D:\Game Files\Osu!",
            @"C:\Game Files\Osu!",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!"),
            @"C:\Program Files (x86)\osu!",
            @"C:\osu!"
        };

        foreach (var path in candidates)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "osu!.db")))
            {
                return path;
            }
        }

        return string.Empty;
    }

    public async Task<bool> LoadFromOsuAsync(string osuDirectory, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(osuDirectory) || !Directory.Exists(osuDirectory))
        {
            return false;
        }

        string dbPath = Path.Combine(osuDirectory, "osu!.db");
        if (!File.Exists(dbPath))
        {
            return false;
        }

        string songsDirectory = Path.Combine(osuDirectory, "Songs");
        if (!Directory.Exists(songsDirectory))
        {
            songsDirectory = osuDirectory;
        }

        progress?.Report("Leyendo osu!.db...");
        await ArtistSplitterService.Instance.LoadSettingsAsync();

        List<OsuBeatmapRaw> rawMaps;
        try
        {
            rawMaps = await Task.Run(() => ReadOsuDb(dbPath));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OsuLibraryService] Error reading osu!.db: {ex.Message}");
            return false;
        }

        progress?.Report("Procesando canciones de osu!...");

        var tracks = await Task.Run(() =>
        {
            // Group by FolderName or (Artist + Title + AudioFilename) to avoid duplicates per difficulty
            var distinctGroups = new Dictionary<string, OsuBeatmapRaw>(StringComparer.OrdinalIgnoreCase);

            foreach (var bm in rawMaps)
            {
                if (string.IsNullOrWhiteSpace(bm.AudioFilename) || string.IsNullOrWhiteSpace(bm.FolderName))
                    continue;

                string groupKey = $"{bm.FolderName}____{bm.AudioFilename}";
                if (!distinctGroups.ContainsKey(groupKey))
                {
                    distinctGroups[groupKey] = bm;
                }
            }

            var result = new List<Track>();
            long trackId = 1000000; // Offset to keep independent from normal library

            foreach (var kv in distinctGroups)
            {
                var bm = kv.Value;
                string folderPath = Path.Combine(songsDirectory, bm.FolderName);
                string audioPath = Path.Combine(folderPath, bm.AudioFilename);

                if (!File.Exists(audioPath))
                {
                    continue;
                }

                string displayArtist;
                string displayTitle;

                if (PreferUnicodeMetadata)
                {
                    displayArtist = !string.IsNullOrWhiteSpace(bm.ArtistUnicode) ? bm.ArtistUnicode : (!string.IsNullOrWhiteSpace(bm.Artist) ? bm.Artist : "Unknown Artist");
                    displayTitle = !string.IsNullOrWhiteSpace(bm.TitleUnicode) ? bm.TitleUnicode : (!string.IsNullOrWhiteSpace(bm.Title) ? bm.Title : Path.GetFileNameWithoutExtension(bm.AudioFilename));
                }
                else
                {
                    displayArtist = !string.IsNullOrWhiteSpace(bm.Artist) ? bm.Artist : (!string.IsNullOrWhiteSpace(bm.ArtistUnicode) ? bm.ArtistUnicode : "Unknown Artist");
                    displayTitle = !string.IsNullOrWhiteSpace(bm.Title) ? bm.Title : (!string.IsNullOrWhiteSpace(bm.TitleUnicode) ? bm.TitleUnicode : Path.GetFileNameWithoutExtension(bm.AudioFilename));
                }

                // Find background image and optional video
                var (artworkPath, videoPath, videoOffset) = FindMediaAssets(folderPath, bm.OsuFilename);

                var track = new Track
                {
                    Id = ++trackId,
                    FilePath = audioPath,
                    Title = displayTitle,
                    Artist = displayArtist,
                    Album = string.Empty, // Left empty instead of "osu! Beatmaps" so scrobblers fetch real album
                    AlbumArtist = displayArtist,
                    Genre = !string.IsNullOrWhiteSpace(bm.Source) ? bm.Source : "osu!",
                    Duration = TimeSpan.FromMilliseconds(Math.Max(0, bm.TotalTimeMs)),
                    FileFormat = Path.GetExtension(audioPath).TrimStart('.').ToUpperInvariant(),
                    ArtworkPath = artworkPath,
                    VideoPath = videoPath,
                    VideoOffsetMs = videoOffset,
                    OsuSource = bm.Source ?? string.Empty,
                    OsuArtistRoumaji = bm.Artist ?? string.Empty,
                    OsuTitleRoumaji = bm.Title ?? string.Empty,
                    OsuArtistUnicode = bm.ArtistUnicode ?? string.Empty,
                    OsuTitleUnicode = bm.TitleUnicode ?? string.Empty,
                    DateAdded = DateTime.UtcNow
                };

                // Grouping character
                track.FirstLetterGroup = !string.IsNullOrWhiteSpace(displayTitle)
                    ? char.ToUpperInvariant(displayTitle[0]).ToString()
                    : "#";

                result.Add(track);
            }

            result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
            return result;
        });

        _osuTracks.Clear();
        _osuTracks.AddRange(tracks);

        RebuildArtistsFromTracks();

        IsLoaded = true;
        LibraryUpdated?.Invoke();
        progress?.Report($"Listo ({_osuTracks.Count} canciones cargadas)");

        _ = Task.Run(() => Online.ArtistEnrichmentService.Instance.EnqueueAndStartAsync(force: false));

        return true;
    }

    /// <summary>
    /// Fast in-memory re-index of osu! artists using the latest ArtistSplitterService separators & exceptions
    /// over Artist (and OsuArtistRoumaji when applicable).
    /// </summary>
    public void ReindexOsuArtists()
    {
        if (_osuTracks.Count == 0) return;

        RebuildArtistsFromTracks();
        LibraryUpdated?.Invoke();
    }

    private void RebuildArtistsFromTracks()
    {
        var artistTracksMap = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
        var artistCanonicalName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in _osuTracks)
        {
            var splitNames = SplitOsuTrackArtists(t);

            foreach (var name in splitNames)
            {
                if (!artistTracksMap.TryGetValue(name, out var list))
                {
                    list = new List<Track>();
                    artistTracksMap[name] = list;
                    artistCanonicalName[name] = name;
                }
                list.Add(t);
            }
        }

        var artistGroups = new List<ArtistModel>(artistTracksMap.Count);
        foreach (var kvp in artistTracksMap)
        {
            string artistName = artistCanonicalName[kvp.Key];
            var list = kvp.Value;
            var firstWithArt = list.FirstOrDefault(t => !string.IsNullOrEmpty(t.ArtworkPath));
            artistGroups.Add(new ArtistModel
            {
                Name = artistName,
                AlphaGroupKey = Helpers.AlphaGroupHelper.GetAlphaGroupKey(artistName),
                AvatarPath = firstWithArt?.ArtworkPath,
                Tracks = list,
                PersistedTrackCount = list.Count
            });
        }

        artistGroups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        _osuArtists.Clear();
        _osuArtists.AddRange(artistGroups);
    }

    public static List<string> SplitOsuTrackArtists(Track t)
    {
        var splitter = ArtistSplitterService.Instance;
        string primary = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist : t.OsuArtistRoumaji;
        if (string.IsNullOrWhiteSpace(primary))
        {
            return new List<string> { "Desconocido" };
        }

        // If OsuArtistRoumaji is present and differs from Artist (e.g. Unicode mode),
        // check if an exception in Ajustes matches OsuArtistRoumaji or split both consistently.
        if (!string.IsNullOrWhiteSpace(t.OsuArtistRoumaji) &&
            !string.Equals(primary, t.OsuArtistRoumaji, StringComparison.OrdinalIgnoreCase))
        {
            bool roumajiMatchesException = false;
            foreach (var exc in splitter.Exceptions)
            {
                if (!string.IsNullOrWhiteSpace(exc) &&
                    t.OsuArtistRoumaji.Contains(exc, StringComparison.OrdinalIgnoreCase))
                {
                    roumajiMatchesException = true;
                    break;
                }
            }

            if (roumajiMatchesException)
            {
                var roumajiSplit = splitter.SplitArtists(t.OsuArtistRoumaji);
                if (roumajiSplit.Count > 0) return roumajiSplit;
            }
        }

        var names = splitter.SplitArtists(primary);
        if (names.Count == 0) names.Add(primary);
        return names;
    }

    private static (string ArtworkPath, string VideoPath, int VideoOffsetMs) FindMediaAssets(string folderPath, string osuFilename)
    {
        string artwork = string.Empty;
        string video = string.Empty;
        int videoOffset = 0;

        try
        {
            // 1. Try reading .osu file [Events] section
            if (!string.IsNullOrWhiteSpace(osuFilename))
            {
                string osuFilePath = Path.Combine(folderPath, osuFilename);
                if (File.Exists(osuFilePath))
                {
                    using var sr = new StreamReader(osuFilePath, Encoding.UTF8);
                    string? line;
                    bool inEvents = false;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("[Events]", StringComparison.OrdinalIgnoreCase))
                        {
                            inEvents = true;
                            continue;
                        }

                        if (inEvents)
                        {
                            if (line.StartsWith("[") && line.EndsWith("]"))
                                break; // left Events section

                            // Video line syntax: Video,-200,"video.mp4" or 1,-200,"video.mp4"
                            if (line.StartsWith("Video,", StringComparison.OrdinalIgnoreCase) || line.StartsWith("1,"))
                            {
                                int firstQuote = line.IndexOf('"');
                                int lastQuote = line.LastIndexOf('"');
                                if (firstQuote >= 0 && lastQuote > firstQuote)
                                {
                                    string vidRel = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                                    string vidFull = Path.Combine(folderPath, vidRel);
                                    if (File.Exists(vidFull) && IsVideoFile(vidFull))
                                    {
                                        video = vidFull;
                                        // Parse offset if present
                                        string[] parts = line.Substring(0, firstQuote).Split(',');
                                        if (parts.Length > 1 && int.TryParse(parts[1], out int parsedOffset))
                                        {
                                            videoOffset = parsedOffset;
                                        }
                                    }
                                }
                            }
                            // Background line syntax: 0,0,"bg.jpg",0,0
                            else if (line.StartsWith("0,0,") || line.StartsWith("0,"))
                            {
                                int firstQuote = line.IndexOf('"');
                                int lastQuote = line.LastIndexOf('"');
                                if (firstQuote >= 0 && lastQuote > firstQuote)
                                {
                                    string bgRel = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                                    string bgFull = Path.Combine(folderPath, bgRel);
                                    if (File.Exists(bgFull) && IsImageFile(bgFull))
                                    {
                                        artwork = bgFull;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 2. Fallbacks: Search files in the folder if not found in .osu
            var dir = new DirectoryInfo(folderPath);
            if (dir.Exists)
            {
                if (string.IsNullOrEmpty(video))
                {
                    foreach (var file in dir.EnumerateFiles())
                    {
                        if (IsVideoFile(file.FullName))
                        {
                            video = file.FullName;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(artwork))
                {
                    FileInfo? largestImg = null;
                    long maxLen = -1;
                    foreach (var file in dir.EnumerateFiles())
                    {
                        if (IsImageFile(file.FullName) && file.Length > maxLen)
                        {
                            maxLen = file.Length;
                            largestImg = file;
                        }
                    }

                    if (largestImg != null)
                    {
                        artwork = largestImg.FullName;
                    }
                }
            }
        }
        catch
        {
            // Ignore filesystem errors for smooth loading
        }

        return (artwork, video, videoOffset);
    }

    private static bool IsVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".avi" or ".flv" or ".mkv" or ".wmv" or ".mov";
    }

    private static bool IsImageFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static List<OsuBeatmapRaw> ReadOsuDb(string dbPath)
    {
        var list = new List<OsuBeatmapRaw>();

        using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs, Encoding.UTF8);

        int version = br.ReadInt32();
        int folderCount = br.ReadInt32();
        bool accountUnlocked = br.ReadBoolean();
        long date = br.ReadInt64();
        string playerName = ReadOsuString(br);
        int beatmapCount = br.ReadInt32();

        for (int i = 0; i < beatmapCount; i++)
        {
            if (version < 20191106) br.ReadInt32();

            string artist = ReadOsuString(br);
            string artistU = ReadOsuString(br);
            string title = ReadOsuString(br);
            string titleU = ReadOsuString(br);
            string creator = ReadOsuString(br);
            string diff = ReadOsuString(br);
            string audio = ReadOsuString(br);
            string md5 = ReadOsuString(br);
            string osuFile = ReadOsuString(br);
            byte rankedStatus = br.ReadByte();
            short hitCircles = br.ReadInt16();
            short sliders = br.ReadInt16();
            short spinners = br.ReadInt16();
            long lastModified = br.ReadInt64();

            if (version >= 20140609)
            {
                br.ReadSingle(); // AR
                br.ReadSingle(); // CS
                br.ReadSingle(); // HP
                br.ReadSingle(); // OD
            }
            else
            {
                br.ReadByte();
                br.ReadByte();
                br.ReadByte();
                br.ReadByte();
            }
            double sliderVelocity = br.ReadDouble();

            if (version >= 20140609)
            {
                SkipIntPairs(br); // Standard
                SkipIntPairs(br); // Taiko
                SkipIntPairs(br); // CTB
                SkipIntPairs(br); // Mania
            }

            int drainTime = br.ReadInt32();
            int totalTime = br.ReadInt32();
            int audioPreview = br.ReadInt32();

            int timingPointsCount = br.ReadInt32();
            for (int t = 0; t < timingPointsCount; t++)
            {
                br.ReadDouble();
                br.ReadDouble();
                br.ReadBoolean();
            }

            int beatmapId = br.ReadInt32();
            int beatmapSetId = br.ReadInt32();
            int threadId = br.ReadInt32();
            br.ReadByte(); // gradeOsu
            br.ReadByte(); // gradeTaiko
            br.ReadByte(); // gradeCtb
            br.ReadByte(); // gradeMania
            br.ReadInt16(); // localOffset
            br.ReadSingle(); // stackLeniency
            br.ReadByte(); // mode
            string source = ReadOsuString(br);
            string tags = ReadOsuString(br);
            br.ReadInt16(); // onlineOffset
            ReadOsuString(br); // titleFont
            br.ReadBoolean(); // unplayed
            br.ReadInt64(); // lastPlayed
            br.ReadBoolean(); // isOsz2
            string folderName = ReadOsuString(br);
            br.ReadInt64(); // lastChecked
            br.ReadBoolean(); // ignoreHitsounds
            br.ReadBoolean(); // ignoreSkin
            br.ReadBoolean(); // ignoreSb
            br.ReadBoolean(); // ignoreVideo
            br.ReadBoolean(); // visualOverride
            if (version < 20140609) br.ReadInt16();
            br.ReadInt32(); // lastModTime
            br.ReadByte(); // scrollSpeed

            list.Add(new OsuBeatmapRaw
            {
                Artist = artist,
                ArtistUnicode = artistU,
                Title = title,
                TitleUnicode = titleU,
                Creator = creator,
                Difficulty = diff,
                AudioFilename = audio,
                OsuFilename = osuFile,
                FolderName = folderName,
                TotalTimeMs = totalTime,
                Source = source,
                Tags = tags,
                BeatmapSetId = beatmapSetId
            });
        }

        return list;
    }

    private static void SkipIntPairs(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            reader.ReadByte(); // 0x08
            reader.ReadInt32(); // mod
            byte valType = reader.ReadByte(); // 0x0c (float) or 0x0d (double)
            if (valType == 0x0c)
            {
                reader.ReadSingle();
            }
            else if (valType == 0x0d)
            {
                reader.ReadDouble();
            }
        }
    }

    private static string ReadOsuString(BinaryReader reader)
    {
        byte type = reader.ReadByte();
        if (type == 0) return string.Empty;
        if (type == 0x0b)
        {
            return reader.ReadString();
        }
        return string.Empty;
    }
}
