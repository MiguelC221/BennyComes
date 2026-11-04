using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BenyComes.Services.Library;

public class ArtistAliasStoreData
{
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RejectedPairs { get; set; } = new();
}

/// <summary>
/// Gestiona y persiste los alias y unificaciones de nombres de artistas (ej. 'かめりあ' -> 'Camellia')
/// tanto para la biblioteca local (Mi Música) como para la biblioteca de osu!.
/// </summary>
public class ArtistAliasService
{
    private static readonly Lazy<ArtistAliasService> _instance = new(() => new ArtistAliasService());
    public static ArtistAliasService Instance => _instance.Value;

    private readonly object _syncLock = new();
    private readonly string _storagePath;
    private readonly string _legacyOsuStoragePath;

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rejectedPairs = new(StringComparer.OrdinalIgnoreCase);

    public event Action? AliasesChanged;

    private ArtistAliasService()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayer"
        );
        Directory.CreateDirectory(appData);
        _storagePath = Path.Combine(appData, "artist_aliases.json");
        _legacyOsuStoragePath = Path.Combine(appData, "osu_artist_aliases.json");
        Load();
    }

    public static string MakePairKey(string a, string b)
        => $"{a.Trim()}||{b.Trim()}";

    public void Load()
    {
        lock (_syncLock)
        {
            try
            {
                _aliases.Clear();
                _rejectedPairs.Clear();

                // 1. Cargar desde el archivo principal
                if (File.Exists(_storagePath))
                {
                    string json = File.ReadAllText(_storagePath);
                    var data = JsonSerializer.Deserialize<ArtistAliasStoreData>(json);
                    if (data?.Aliases != null)
                    {
                        foreach (var kv in data.Aliases)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                            {
                                _aliases[kv.Key.Trim()] = kv.Value.Trim();
                            }
                        }
                    }
                    if (data?.RejectedPairs != null)
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

                // 2. Si existe el archivo legado de osu!, fusionar cualquier alias previo no presente
                if (File.Exists(_legacyOsuStoragePath))
                {
                    string legacyJson = File.ReadAllText(_legacyOsuStoragePath);
                    var legacyData = JsonSerializer.Deserialize<ArtistAliasStoreData>(legacyJson);
                    if (legacyData?.Aliases != null)
                    {
                        foreach (var kv in legacyData.Aliases)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                            {
                                _aliases.TryAdd(kv.Key.Trim(), kv.Value.Trim());
                            }
                        }
                    }
                    if (legacyData?.RejectedPairs != null)
                    {
                        foreach (var pair in legacyData.RejectedPairs)
                        {
                            if (!string.IsNullOrWhiteSpace(pair))
                            {
                                _rejectedPairs.Add(pair.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistAliasService] Error loading aliases: {ex.Message}");
            }
        }
    }

    public void Save()
    {
        lock (_syncLock)
        {
            try
            {
                var data = new ArtistAliasStoreData
                {
                    Aliases = new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase),
                    RejectedPairs = new List<string>(_rejectedPairs)
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_storagePath, json);
                // Guardar también en la ruta heredada para compatibilidad total
                File.WriteAllText(_legacyOsuStoragePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistAliasService] Error saving aliases: {ex.Message}");
            }
        }
    }

    public string GetCanonicalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
        string current = name.Trim();
        lock (_syncLock)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (_aliases.TryGetValue(current, out var next) && !string.IsNullOrWhiteSpace(next))
            {
                if (!visited.Add(current)) break;
                current = next.Trim();
            }
        }
        return current;
    }

    public bool TryGetCanonical(string alias, out string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            canonicalName = string.Empty;
            return false;
        }

        string canonical = GetCanonicalName(alias);
        if (!string.Equals(alias.Trim(), canonical, StringComparison.OrdinalIgnoreCase))
        {
            canonicalName = canonical;
            return true;
        }

        canonicalName = string.Empty;
        return false;
    }

    public List<string> GetAliasesFor(string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(canonicalName)) return new List<string>();
        string target = canonicalName.Trim();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_syncLock)
        {
            foreach (var kvp in _aliases)
            {
                if (string.Equals(GetCanonicalName(kvp.Key), target, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(kvp.Key, target, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(kvp.Key);
                    }
                }
            }
        }

        return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetAlias(string aliasName, string canonicalName, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(aliasName) || string.IsNullOrWhiteSpace(canonicalName)) return;
        string a = aliasName.Trim();
        string c = canonicalName.Trim();
        if (string.Equals(a, c, StringComparison.OrdinalIgnoreCase)) return;

        lock (_syncLock)
        {
            string resolvedC = GetCanonicalName(c);
            if (string.Equals(a, resolvedC, StringComparison.OrdinalIgnoreCase))
            {
                _aliases.Remove(c);
                resolvedC = c;
            }

            _aliases[a] = resolvedC;
            _rejectedPairs.Remove(MakePairKey(a, resolvedC));
            _rejectedPairs.Remove(MakePairKey(resolvedC, a));

            if (saveImmediately) Save();
        }

        AliasesChanged?.Invoke();
    }

    public void RemoveAlias(string aliasName, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(aliasName)) return;
        string a = aliasName.Trim();

        lock (_syncLock)
        {
            if (_aliases.Remove(a))
            {
                if (saveImmediately) Save();
            }
        }

        AliasesChanged?.Invoke();
    }

    public Dictionary<string, string> GetAllAliases()
    {
        lock (_syncLock)
        {
            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsRejected(string a, string b)
    {
        lock (_syncLock)
        {
            return _rejectedPairs.Contains(MakePairKey(a, b)) ||
                   _rejectedPairs.Contains(MakePairKey(b, a));
        }
    }

    public void RejectPair(string a, string b, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
        lock (_syncLock)
        {
            string key = MakePairKey(a, b);
            _rejectedPairs.Add(key);
            _aliases.Remove(a.Trim());
            _aliases.Remove(b.Trim());

            if (saveImmediately) Save();
        }

        AliasesChanged?.Invoke();
    }
}
