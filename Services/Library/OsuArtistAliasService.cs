using System;
using System.Collections.Generic;

namespace BenyComes.Services.Library;

/// <summary>
/// Capa de compatibilidad para la biblioteca de osu! que delega en el servicio unificado <see cref="ArtistAliasService"/>.
/// </summary>
public class OsuArtistAliasService
{
    private static readonly Lazy<OsuArtistAliasService> _instance = new(() => new OsuArtistAliasService());
    public static OsuArtistAliasService Instance => _instance.Value;

    private readonly ArtistAliasService _service = ArtistAliasService.Instance;

    public static string MakePairKey(string unicodeName, string romajiName)
        => ArtistAliasService.MakePairKey(unicodeName, romajiName);

    public void Load() => _service.Load();

    public void Save() => _service.Save();

    public bool TryGetCanonical(string alias, out string canonicalName)
        => _service.TryGetCanonical(alias, out canonicalName);

    public bool IsRejected(string unicodeName, string romajiName)
        => _service.IsRejected(unicodeName, romajiName);

    public void SetAlias(string aliasName, string canonicalRomajiName, bool saveImmediately = true)
        => _service.SetAlias(aliasName, canonicalRomajiName, saveImmediately);

    public void RejectPair(string unicodeName, string romajiName, bool saveImmediately = true)
        => _service.RejectPair(unicodeName, romajiName, saveImmediately);
}
