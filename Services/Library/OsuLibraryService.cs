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
    private readonly List<DoubtfulArtistPair> _doubtfulArtists = new();

    public IReadOnlyList<Track> Tracks => _osuTracks;
    public IReadOnlyList<ArtistModel> Artists => _osuArtists;
    public IReadOnlyList<DoubtfulArtistPair> DoubtfulArtists => _doubtfulArtists;

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
        Data.Favoritos.Instance.SyncTracksFavoriteState(_osuTracks);

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
        var aliasService = OsuArtistAliasService.Instance;
        aliasService.Load();

        // 1. Apply ArtistSplitterService before unifying and collect (Unicode/Artist, Romaji) pairs
        var perTrackPairs = new List<List<(string UnicodeOrDisplay, string Romaji)>>(_osuTracks.Count);
        var distinctPairTrackCounts = new Dictionary<string, (string Unicode, string Romaji, int Count)>(StringComparer.OrdinalIgnoreCase);
        var unicodeToRomajis = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var romajiToUnicodes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in _osuTracks)
        {
            var pairs = ExtractSplitArtistPairs(t);
            perTrackPairs.Add(pairs);

            foreach (var (u, r) in pairs)
            {
                if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(r)) continue;
                if (string.Equals(u, r, StringComparison.OrdinalIgnoreCase)) continue;
                if (aliasService.IsRejected(u, r)) continue;

                string key = OsuArtistAliasService.MakePairKey(u, r);
                if (distinctPairTrackCounts.TryGetValue(key, out var existing))
                {
                    distinctPairTrackCounts[key] = (existing.Unicode, existing.Romaji, existing.Count + 1);
                }
                else
                {
                    distinctPairTrackCounts[key] = (u, r, 1);
                }

                if (!unicodeToRomajis.TryGetValue(u, out var rSet))
                {
                    rSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    unicodeToRomajis[u] = rSet;
                }
                rSet.Add(r);

                if (!romajiToUnicodes.TryGetValue(r, out var uSet))
                {
                    uSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    romajiToUnicodes[r] = uSet;
                }
                uSet.Add(u);
            }
        }

        // 2. Build active alias map (Unicode/alias -> Canonical Romaji) and detect doubtful pairs
        var activeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalAltNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var doubtfulList = new List<DoubtfulArtistPair>();
        bool newAliasesPersisted = false;

        void RegisterAlias(string alias, string canonicalRomaji)
        {
            if (string.Equals(alias, canonicalRomaji, StringComparison.OrdinalIgnoreCase)) return;
            activeAliases[alias] = canonicalRomaji;
            if (!canonicalAltNames.TryGetValue(canonicalRomaji, out var alts))
            {
                alts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                canonicalAltNames[canonicalRomaji] = alts;
            }
            alts.Add(alias);
        }

        foreach (var kv in distinctPairTrackCounts.Values)
        {
            string u = kv.Unicode;
            string r = kv.Romaji;

            // Check if already resolved by persisted alias
            if (aliasService.TryGetCanonical(u, out var persistedCanonical))
            {
                RegisterAlias(u, persistedCanonical);
                continue;
            }

            int rCountForU = unicodeToRomajis.TryGetValue(u, out var rSet) ? rSet.Count : 0;
            int uCountForR = romajiToUnicodes.TryGetValue(r, out var uSet) ? uSet.Count : 0;

            if (rCountForU == 1 && uCountForR == 1)
            {
                // Unambiguous 1-to-1 pair: unify under Romaji and persist
                RegisterAlias(u, r);
                aliasService.SetAlias(u, r, saveImmediately: false);
                newAliasesPersisted = true;
            }
            else
            {
                // Doubtful pair: 1 Unicode with multiple Romajis or 1 Romaji with multiple Unicodes
                string reason = rCountForU > 1
                    ? $"'{u}' aparece con {rCountForU} nombres romaji ({string.Join(", ", unicodeToRomajis[u])})"
                    : $"'{r}' aparece con {uCountForR} nombres unicode ({string.Join(", ", romajiToUnicodes[r])})";

                doubtfulList.Add(new DoubtfulArtistPair
                {
                    UnicodeName = u,
                    RomajiName = r,
                    TrackCount = kv.Count,
                    Reason = reason
                });
            }
        }

        if (newAliasesPersisted)
        {
            aliasService.Save();
        }

        // 3. Group tracks into ArtistModel using canonical Romaji name and AltNames
        var artistTracksMap = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
        var artistTrackIdsMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var artistCanonicalName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddTrackToArtist(string artistName, Track track)
        {
            if (!artistTracksMap.TryGetValue(artistName, out var list))
            {
                list = new List<Track>();
                artistTracksMap[artistName] = list;
                artistTrackIdsMap[artistName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                artistCanonicalName[artistName] = artistName;
            }
            else if (HasNonAscii(artistCanonicalName[artistName]) && !HasNonAscii(artistName))
            {
                artistCanonicalName[artistName] = artistName;
            }

            if (artistTrackIdsMap[artistName].Add(track.FilePath))
            {
                list.Add(track);
            }
        }

        for (int i = 0; i < _osuTracks.Count; i++)
        {
            var t = _osuTracks[i];
            var pairs = perTrackPairs[i];

            foreach (var (u, r) in pairs)
            {
                // Check persisted alias even for tracks where u == r (e.g. track only had Unicode in both fields)
                if (activeAliases.TryGetValue(u, out var mappedFromU))
                {
                    AddTrackToArtist(mappedFromU, t);
                }
                else if (aliasService.TryGetCanonical(u, out var savedFromU))
                {
                    RegisterAlias(u, savedFromU);
                    AddTrackToArtist(savedFromU, t);
                }
                else if (string.Equals(u, r, StringComparison.OrdinalIgnoreCase))
                {
                    AddTrackToArtist(r, t);
                }
                else
                {
                    // Uncombined (either rejected or doubtful): keep both separate until user decides
                    AddTrackToArtist(r, t);
                    AddTrackToArtist(u, t);
                }
            }
        }

        var artistGroups = new List<ArtistModel>(artistTracksMap.Count);
        foreach (var kvp in artistTracksMap)
        {
            string artistName = artistCanonicalName[kvp.Key];
            var list = kvp.Value;
            var firstWithArt = list.FirstOrDefault(t => !string.IsNullOrEmpty(t.ArtworkPath));
            var altNames = canonicalAltNames.TryGetValue(artistName, out var alts)
                ? alts.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<string>();

            artistGroups.Add(new ArtistModel
            {
                Name = artistName,
                AltNames = altNames,
                AlphaGroupKey = Helpers.AlphaGroupHelper.GetAlphaGroupKey(artistName),
                AvatarPath = firstWithArt?.ArtworkPath,
                Tracks = list,
                PersistedTrackCount = list.Count
            });
        }

        artistGroups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        _osuArtists.Clear();
        _osuArtists.AddRange(artistGroups);

        _doubtfulArtists.Clear();
        _doubtfulArtists.AddRange(doubtfulList);
    }

    private static bool HasNonAscii(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] > 127) return true;
        }
        return false;
    }

    /// <summary>
    /// Applies ArtistSplitterService to both Artist/OsuArtistUnicode and OsuArtistRoumaji before unification,
    /// returning aligned (UnicodeOrDisplay, Romaji) pairs for the track.
    /// </summary>
    private static List<(string UnicodeOrDisplay, string Romaji)> ExtractSplitArtistPairs(Track t)
    {
        var splitter = ArtistSplitterService.Instance;

        string rawUni = !string.IsNullOrWhiteSpace(t.OsuArtistUnicode) ? t.OsuArtistUnicode : t.Artist;
        string rawRom = !string.IsNullOrWhiteSpace(t.OsuArtistRoumaji) ? t.OsuArtistRoumaji : t.Artist;

        if (string.IsNullOrWhiteSpace(rawUni) && string.IsNullOrWhiteSpace(rawRom))
        {
            return new List<(string, string)> { ("Desconocido", "Desconocido") };
        }
        if (string.IsNullOrWhiteSpace(rawUni)) rawUni = rawRom;
        if (string.IsNullOrWhiteSpace(rawRom)) rawRom = rawUni;

        var romSplit = splitter.SplitArtists(rawRom);
        var uniSplit = splitter.SplitArtists(rawUni);

        if (romSplit.Count == 0) romSplit.Add(rawRom.Trim());
        if (uniSplit.Count == 0) uniSplit.Add(rawUni.Trim());

        var result = new List<(string, string)>();

        if (uniSplit.Count == romSplit.Count)
        {
            for (int i = 0; i < romSplit.Count; i++)
            {
                string u = uniSplit[i];
                string r = romSplit[i];
                // Ensure the side with non-ASCII is treated as Unicode and ASCII side as Romaji if swapped
                if (!HasNonAscii(u) && HasNonAscii(r))
                {
                    (u, r) = (r, u);
                }
                result.Add((u, r));
            }
        }
        else if (romSplit.Count == 1 && uniSplit.Count > 1 && MatchesAnyException(rawRom, splitter.Exceptions))
        {
            // An exception in Ajustes protected the Romaji name, so keep the Unicode name unsplit as well
            result.Add((rawUni.Trim(), romSplit[0]));
        }
        else
        {
            // Different split counts: do not guess cross-collaborator pairings
            foreach (var r in romSplit)
            {
                result.Add((r, r));
            }
            foreach (var u in uniSplit)
            {
                if (!romSplit.Contains(u, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add((u, u));
                }
            }
        }

        return result;
    }

    private static bool MatchesAnyException(string text, IEnumerable<string> exceptions)
    {
        foreach (var exc in exceptions)
        {
            if (!string.IsNullOrWhiteSpace(exc) && text.Contains(exc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static List<string> SplitOsuTrackArtists(Track t)
    {
        var pairs = ExtractSplitArtistPairs(t);
        var names = new List<string>(pairs.Count);
        foreach (var (_, r) in pairs)
        {
            if (!names.Contains(r, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(r);
            }
        }
        return names.Count > 0 ? names : new List<string> { "Desconocido" };
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
