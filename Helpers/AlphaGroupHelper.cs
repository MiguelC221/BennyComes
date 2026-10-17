using System;
using System.Collections.Generic;
using System.Text;

namespace BenyComes.Helpers;

public static class AlphaGroupHelper
{
    public static readonly string[] Alphabet = new[]
    {
        "#", "&",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "🌐"
    };

    public static string GetAlphaGroupKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "#";
        text = text.TrimStart(' ', '"', '\'', '“', '”', '(', '[', '¿', '¡', '【', '「', '『');
        if (string.IsNullOrEmpty(text)) return "#";

        char first = text[0];
        if (first == '&') return "&";
        if (char.IsDigit(first)) return "#";

        // Normalizar tildes y caracteres diacríticos (ej. Á -> A, É -> E)
        string norm = text.Normalize(NormalizationForm.FormD);
        char normChar = norm[0];

        if ((normChar >= 'A' && normChar <= 'Z') || (normChar >= 'a' && normChar <= 'z'))
        {
            return char.ToUpperInvariant(normChar).ToString();
        }

        // Caracteres CJK, no latinos o símbolos especiales
        if (normChar > 127)
        {
            return "🌐";
        }

        return "#";
    }

    private static readonly Dictionary<string, int> GroupRanks = BuildRanks();

    private static Dictionary<string, int> BuildRanks()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Alphabet.Length; i++) d[Alphabet[i]] = i;
        return d;
    }

    /// <summary>Position of the group in # &amp; A-Z 🌐 order.</summary>
    public static int GetGroupRank(string? groupKey)
        => groupKey != null && GroupRanks.TryGetValue(groupKey, out var r) ? r : 0;

    /// <summary>Accent-insensitive key with leading punctuation removed, to compare with Ordinal(IgnoreCase).</summary>
    public static string GetSortKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.TrimStart(' ', '"', '\'', '“', '”', '(', '[', '¿', '¡', '【', '「', '『', '.', '-', '_', '!');
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Fast lowercase accent-insensitive string for instant search indexing and matching.</summary>
    public static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
