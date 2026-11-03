using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BenyComes.Services.Library;

public class OsuArtistAliasStoreData
{
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RejectedPairs { get; set; } = new();
}

/// <summary>
/// Persists canonical artist aliases (Unicode/alias -> Romaji canonical name) and user decisions
/// for doubtful artist pairs so re-indexing always respects them.
/// </summary>
public class OsuArtistAliasService
{
    private static readonly Lazy<OsuArtistAliasService> _instance = new(() => new OsuArtistAliasService());
    public static OsuArtistAliasService Instance => _instance.Value;

    private readonly object _syncLock = new();
    private readonly string _storagePath;

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rejectedPairs = new(StringComparer.OrdinalIgnoreCase);

    private OsuArtistAliasService()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayer"
        );
        Directory.CreateDirectory(appData);
        _storagePath = Path.Combine(appData, "osu_artist_aliases.json");
        Load();
    }

    public static string MakePairKey(string unicodeName, string romajiName)
        => $"{unicodeName.Trim()}||{romajiName.Trim()}";

    public void Load()
    {
        lock (_syncLock)
        {
            try
            {
                if (!File.Exists(_storagePath)) return;
                string json = File.ReadAllText(_storagePath);
                var data = JsonSerializer.Deserialize<OsuArtistAliasStoreData>(json);
                if (data == null) return;

                _aliases.Clear();
                if (data.Aliases != null)
                {
                    foreach (var kv in data.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        {
                            _aliases[kv.Key.Trim()] = kv.Value.Trim();
                        }
                    }
                }

                _rejectedPairs.Clear();
                if (data.RejectedPairs != null)
                {
                    foreach (var pair in data.RejectedPairs)
                    {
                        if (!string.IsNullOrWhiteSpace(pair))
                        {
                            _rejectedPairs.Add(pair.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OsuArtistAliasService] Error loading aliases: {ex.Message}");
            }
        }
    }

    public void Save()
    {
        lock (_syncLock)
        {
            try
            {
                var data = new OsuArtistAliasStoreData
                {
                    Aliases = new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase),
                    RejectedPairs = new List<string>(_rejectedPairs)
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OsuArtistAliasService] Error saving aliases: {ex.Message}");
            }
        }
    }

    public bool TryGetCanonical(string alias, out string canonicalName)
    {
        lock (_syncLock)
        {
            if (_aliases.TryGetValue(alias.Trim(), out var target) && !string.IsNullOrWhiteSpace(target))
            {
                canonicalName = target;
                return true;
            }
            canonicalName = string.Empty;
            return false;
        }
    }

    public bool IsRejected(string unicodeName, string romajiName)
    {
        lock (_syncLock)
        {
            return _rejectedPairs.Contains(MakePairKey(unicodeName, romajiName)) ||
                   _rejectedPairs.Contains(MakePairKey(romajiName, unicodeName));
        }
    }

    public void SetAlias(string aliasName, string canonicalRomajiName, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(aliasName) || string.IsNullOrWhiteSpace(canonicalRomajiName)) return;
        lock (_syncLock)
        {
            string a = aliasName.Trim();
            string c = canonicalRomajiName.Trim();
            if (string.Equals(a, c, StringComparison.OrdinalIgnoreCase)) return;

            _aliases[a] = c;
            _rejectedPairs.Remove(MakePairKey(a, c));
            _rejectedPairs.Remove(MakePairKey(c, a));

            if (saveImmediately) Save();
        }
    }

    public void RejectPair(string unicodeName, string romajiName, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(unicodeName) || string.IsNullOrWhiteSpace(romajiName)) return;
        lock (_syncLock)
        {
            string u = unicodeName.Trim();
            string r = romajiName.Trim();
            _rejectedPairs.Add(MakePairKey(u, r));

            if (_aliases.TryGetValue(u, out var existing) && string.Equals(existing, r, StringComparison.OrdinalIgnoreCase))
            {
                _aliases.Remove(u);
            }

            if (saveImmediately) Save();
        }
    }
}
