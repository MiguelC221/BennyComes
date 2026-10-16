using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BenyComes.Data;

namespace BenyComes.Services.Library;

public class ArtistSplitterService
{
    private static readonly Lazy<ArtistSplitterService> _lazy = new(() => new ArtistSplitterService());
    public static ArtistSplitterService Instance => _lazy.Value;

    private readonly LibraryDao _dao = LibraryDao.Instance;

    public ObservableCollection<string> Separators { get; } = new();
    public ObservableCollection<string> Exceptions { get; } = new();

    private readonly List<string> _defaultSeparators = new()
    {
        ",", ";", "&", "/", "feat.", "featuring", "ft.", "vs.", "x"
    };

    private readonly List<string> _defaultExceptions = new()
    {
        "Tyler, The Creator",
        "AC/DC",
        "Earth, Wind & Fire",
        @"Axwell /\ Ingrosso",
        "Rival & CADMIUM",
        "Jim Yosef & Anna Yvette",
        "+44"
    };

    private ArtistSplitterService()
    {
        ResetToDefaults();
    }

    public void ResetToDefaults()
    {
        Action resetAction = () =>
        {
            Separators.Clear();
            foreach (var s in _defaultSeparators)
            {
                Separators.Add(s);
            }

            Exceptions.Clear();
            foreach (var ex in _defaultExceptions)
            {
                Exceptions.Add(ex);
            }
        };

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            app.Dispatcher.Invoke(resetAction);
        else
            resetAction();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            string sepSetting = await _dao.GetSettingAsync("artist_separators", string.Empty);
            string excSetting = await _dao.GetSettingAsync("artist_exceptions", string.Empty);

            var app = System.Windows.Application.Current;
            Action updateCollections = () =>
            {
                if (!string.IsNullOrWhiteSpace(sepSetting))
                {
                    var seps = sepSetting.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    Separators.Clear();
                    foreach (var s in seps) Separators.Add(s);
                }

                if (!string.IsNullOrWhiteSpace(excSetting))
                {
                    var excs = excSetting.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    Exceptions.Clear();
                    foreach (var e in excs) Exceptions.Add(e);
                }
            };

            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(updateCollections);
            else
                updateCollections();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load artist splitter settings: {ex.Message}");
        }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            string sepSetting = string.Join('|', Separators);
            string excSetting = string.Join('|', Exceptions);

            await _dao.SetSettingAsync("artist_separators", sepSetting);
            await _dao.SetSettingAsync("artist_exceptions", excSetting);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save artist splitter settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses an artist string into individual artist names respecting exceptions and separators.
    /// </summary>
    public List<string> SplitArtists(string? rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist))
        {
            return new List<string> { "Artista Desconocido" };
        }

        string text = rawArtist.Trim();
        var tokenMap = new Dictionary<string, string>();
        int tokenIndex = 0;

        // Step 1: Protect exceptions
        // Sort exceptions by length descending to match longest exceptions first
        var sortedExceptions = Exceptions
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .OrderByDescending(e => e.Length)
            .ToList();

        foreach (var exc in sortedExceptions)
        {
            // Case-insensitive replacement
            string pattern = Regex.Escape(exc);
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            while (match.Success)
            {
                string token = $"__EXC_{tokenIndex++}__";
                tokenMap[token] = match.Value; // preserve original casing
                text = text.Substring(0, match.Index) + token + text.Substring(match.Index + match.Length);
                match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            }
        }

        // Step 2: Split text using active separators
        // Sort separators by length descending
        var sortedSeparators = Separators
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .OrderByDescending(s => s.Length)
            .ToList();

        if (sortedSeparators.Count == 0)
        {
            // Restore token and return single artist
            return new List<string> { RestoreTokens(text, tokenMap) };
        }

        // Build regex pattern for splitting:
        // Words like 'feat.', 'ft.', 'vs.', 'x' should match with surrounding boundaries/whitespace
        // Punctuation like ',', ';', '&' can match directly
        var patternParts = new List<string>();
        foreach (var sep in sortedSeparators)
        {
            string escaped = Regex.Escape(sep);
            bool isWord = Regex.IsMatch(sep, @"^\w+(\.\w+)*$");
            if (isWord)
            {
                patternParts.Add($@"(?<=\s|^){escaped}(?=\s|$)");
            }
            else
            {
                patternParts.Add(escaped);
            }
        }

        string splitRegex = string.Join("|", patternParts);
        string[] rawParts = Regex.Split(text, splitRegex, RegexOptions.IgnoreCase);

        // Step 3: Restore tokens and clean up
        var result = new List<string>();
        foreach (var part in rawParts)
        {
            string restored = RestoreTokens(part, tokenMap).Trim();
            // Remove lingering punctuation from ends
            restored = restored.Trim(',', ';', '-', '/', '\\', ' ');
            if (!string.IsNullOrWhiteSpace(restored) && !result.Contains(restored, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(restored);
            }
        }

        return result.Count > 0 ? result : new List<string> { rawArtist.Trim() };
    }

    private string RestoreTokens(string input, Dictionary<string, string> tokenMap)
    {
        string result = input;
        foreach (var (token, original) in tokenMap)
        {
            result = result.Replace(token, original);
        }
        return result;
    }
}
