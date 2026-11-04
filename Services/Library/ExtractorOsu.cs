using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Metadata;
using BenyComes.Services.Online;
using NAudio.Wave;

namespace BenyComes.Services.Library;

/// <summary>
/// Servicio extractor de pistas de osu! hacia la biblioteca local de música del usuario.
/// Exporta pistas de osu! en formato MP3 dentro de Carpeta Música > [Nombre de Playlist],
/// incrusta metadatos reales (Last.fm / osu!) junto a la carátula e indexa la pista a la biblioteca general.
/// </summary>
public static class ExtractorOsu
{
    private static readonly Regex InvalidCharsRegex = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

    /// <summary>
    /// Comprueba si una pista dada procede del juego osu!.
    /// </summary>
    public static bool IsOsuTrack(Track? track)
    {
        if (track == null) return false;

        if (!string.IsNullOrWhiteSpace(track.OsuArtistRoumaji) ||
            !string.IsNullOrWhiteSpace(track.OsuTitleRoumaji) ||
            !string.IsNullOrWhiteSpace(track.OsuSource) ||
            track.HasVideo)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            string pathNorm = track.FilePath.Replace('/', Path.DirectorySeparatorChar);
            if (pathNorm.IndexOf($"{Path.DirectorySeparatorChar}Songs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (OsuLibraryService.Instance.IsLoaded &&
                OsuLibraryService.Instance.Tracks.Any(s => string.Equals(s.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Limpia un nombre para que sea seguro como nombre de archivo o carpeta en Windows.
    /// </summary>
    public static string SanitizeName(string name, string fallback = "Sin Nombre")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        string clean = InvalidCharsRegex.Replace(name.Trim(), "_").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    /// <summary>
    /// Obtiene el directorio base de destino en la carpeta Música del usuario para una lista de reproducción específica.
    /// </summary>
    public static string GetPlaylistDirectory(string playlistName)
    {
        string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(myMusic) || !Directory.Exists(myMusic))
        {
            myMusic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        }
        string safePlaylistName = SanitizeName(playlistName, "Mi Lista");
        string targetDir = Path.Combine(myMusic, safePlaylistName);
        Directory.CreateDirectory(targetDir);
        return targetDir;
    }

    /// <summary>
    /// Extrae y exporta una pista de osu!, convirtiendo o copiando a MP3 con metadatos incrustados y carátula,
    /// e indexándola a la biblioteca principal.
    /// </summary>
    public static async Task<Track> ExtractTrackAsync(Track osuTrack, string playlistName, IProgress<string>? progress = null)
    {
        if (!File.Exists(osuTrack.FilePath))
        {
            return osuTrack;
        }

        string playlistDir = GetPlaylistDirectory(playlistName);

        string displayArtist = !string.IsNullOrWhiteSpace(osuTrack.Artist) ? osuTrack.Artist.Trim() : "Artista Desconocido";
        string displayTitle = !string.IsNullOrWhiteSpace(osuTrack.Title) ? osuTrack.Title.Trim() : Path.GetFileNameWithoutExtension(osuTrack.FilePath);

        string safeArtist = SanitizeName(displayArtist, "Artista");
        string safeTitle = SanitizeName(displayTitle, "Pista");
        string destFileName = $"{safeArtist} - {safeTitle}.mp3";
        string destFilePath = Path.Combine(playlistDir, destFileName);

        // Si ya existe el archivo idéntico extraído previamente, podemos reusarlo
        if (File.Exists(destFilePath))
        {
            var existingTrack = LibraryCacheService.Instance.CachedTracks
                .FirstOrDefault(t => string.Equals(t.FilePath, destFilePath, StringComparison.OrdinalIgnoreCase));

            if (existingTrack != null)
            {
                return existingTrack;
            }
        }

        progress?.Report($"Extrayendo audio de osu!: {displayTitle}...");

        // 1. Exportar audio (copiar si ya es MP3 o convertir vía MediaFoundation)
        await Task.Run(() =>
        {
            string srcExt = Path.GetExtension(osuTrack.FilePath).ToLowerInvariant();
            if (srcExt == ".mp3")
            {
                File.Copy(osuTrack.FilePath, destFilePath, overwrite: true);
            }
            else
            {
                try
                {
                    using var reader = new MediaFoundationReader(osuTrack.FilePath);
                    MediaFoundationEncoder.EncodeToMp3(reader, destFilePath, 192000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtractorOsu] Fallo al codificar a MP3: {ex.Message}. Copiando archivo original.");
                    destFilePath = Path.ChangeExtension(destFilePath, srcExt);
                    File.Copy(osuTrack.FilePath, destFilePath, overwrite: true);
                }
            }
        });

        // 2. Extraer metadatos de Last.fm si están disponibles
        string? fetchedAlbum = null;
        string? fetchedCoverUrl = null;
        if (LastFmService.Instance.IsEnabled && !string.IsNullOrWhiteSpace(LastFmService.Instance.ApiKey))
        {
            try
            {
                var (alb, cov) = await LastFmService.Instance.FetchTrackAlbumInfoAsync(
                    displayArtist,
                    displayTitle,
                    alternateArtist: osuTrack.OsuArtistRoumaji,
                    alternateTitle: osuTrack.OsuTitleRoumaji
                );
                fetchedAlbum = alb;
                fetchedCoverUrl = cov;
            }
            catch { }
        }

        // 3. Determinar carátula local a incrustar
        string? coverToEmbed = null;
        if (!string.IsNullOrWhiteSpace(fetchedCoverUrl))
        {
            try
            {
                coverToEmbed = await LastFmService.Instance.DownloadAndCacheAlbumCoverAsync(
                    displayArtist,
                    fetchedAlbum ?? displayTitle,
                    fetchedCoverUrl
                );
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(coverToEmbed) || !File.Exists(coverToEmbed))
        {
            if (!string.IsNullOrWhiteSpace(osuTrack.ArtworkPath) && File.Exists(osuTrack.ArtworkPath))
            {
                coverToEmbed = osuTrack.ArtworkPath;
            }
        }

        string finalAlbum = !string.IsNullOrWhiteSpace(fetchedAlbum)
            ? fetchedAlbum
            : (!string.IsNullOrWhiteSpace(osuTrack.Album) && osuTrack.Album != "Unknown Album"
                ? osuTrack.Album
                : (!string.IsNullOrWhiteSpace(osuTrack.OsuSource) ? osuTrack.OsuSource : playlistName));

        // 4. Incrustar metadatos ID3 y carátula en el archivo MP3 resultante con TagLib
        try
        {
            using var tagFile = TagLib.File.Create(destFilePath);
            tagFile.Tag.Title = displayTitle;
            tagFile.Tag.Performers = new[] { displayArtist };
            tagFile.Tag.AlbumArtists = new[] { displayArtist };
            tagFile.Tag.Album = finalAlbum;

            if (!string.IsNullOrWhiteSpace(osuTrack.Genre))
            {
                tagFile.Tag.Genres = new[] { osuTrack.Genre };
            }
            if (osuTrack.Year > 0)
            {
                tagFile.Tag.Year = osuTrack.Year;
            }

            if (!string.IsNullOrWhiteSpace(coverToEmbed) && File.Exists(coverToEmbed))
            {
                try
                {
                    string ext = Path.GetExtension(coverToEmbed).ToLowerInvariant();
                    string mimeType = ext switch
                    {
                        ".png" => "image/png",
                        ".webp" => "image/webp",
                        _ => "image/jpeg"
                    };

                    var pic = new TagLib.Picture(coverToEmbed)
                    {
                        Type = TagLib.PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = mimeType
                    };
                    tagFile.Tag.Pictures = new TagLib.IPicture[] { pic };
                }
                catch { }
            }

            tagFile.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractorOsu] Error guardando tags en {destFilePath}: {ex.Message}");
        }

        // 5. Construir el nuevo modelo Track listo para la biblioteca principal
        var extractedTrack = new Track
        {
            FilePath = destFilePath,
            Title = displayTitle,
            Artist = displayArtist,
            Album = finalAlbum,
            AlbumArtist = displayArtist,
            Genre = !string.IsNullOrWhiteSpace(osuTrack.Genre) ? osuTrack.Genre : "osu!",
            Year = osuTrack.Year,
            TrackNumber = osuTrack.TrackNumber,
            Duration = osuTrack.Duration,
            FileFormat = Path.GetExtension(destFilePath).TrimStart('.').ToUpperInvariant(),
            ArtworkPath = coverToEmbed ?? "",
            DateAdded = DateTime.UtcNow,
            FirstLetterGroup = Helpers.AlphaGroupHelper.GetAlphaGroupKey(displayTitle)
        };

        // 6. Registrar en la base de datos principal y en la caché de biblioteca
        try
        {
            long newId = await LibraryDao.Instance.InsertOrUpdateTrackAsync(extractedTrack);
            extractedTrack.Id = newId;
            await LibraryCacheService.Instance.IntegrateIndexedBatchAsync(new[] { extractedTrack }, isFinalBatch: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractorOsu] Error indexando pista extraída: {ex.Message}");
        }

        return extractedTrack;
    }

    /// <summary>
    /// Procesa una lista de pistas, extrayendo y reemplazando cualquier pista procedente de osu!
    /// por su versión extraída en Carpeta Música > [Nombre Playlist].
    /// </summary>
    public static async Task<List<Track>> ProcessAndExtractTracksAsync(
        IEnumerable<Track> tracks,
        string playlistName,
        IProgress<string>? progress = null)
    {
        var result = new List<Track>();
        var list = tracks.ToList();
        int index = 0;
        int total = list.Count;

        foreach (var t in list)
        {
            index++;
            if (IsOsuTrack(t))
            {
                progress?.Report($"Extrayendo canciones de osu! ({index}/{total})...");
                try
                {
                    var extracted = await ExtractTrackAsync(t, playlistName, progress);
                    result.Add(extracted);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtractorOsu] Error al procesar {t.Title}: {ex.Message}");
                    result.Add(t);
                }
            }
            else
            {
                result.Add(t);
            }
        }

        return result;
    }

    /// <summary>
    /// Si se cambia el nombre de la playlist desde la app, renombra la carpeta correspondiente en Carpeta Música
    /// y actualiza las rutas de las pistas asociadas en la base de datos y la memoria.
    /// </summary>
    public static async Task RenamePlaylistFolderAsync(string oldPlaylistName, string newPlaylistName, long playlistId)
    {
        if (string.IsNullOrWhiteSpace(oldPlaylistName) || string.IsNullOrWhiteSpace(newPlaylistName)) return;
        if (string.Equals(oldPlaylistName.Trim(), newPlaylistName.Trim(), StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(myMusic) || !Directory.Exists(myMusic))
            {
                myMusic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            }

            string oldSafe = SanitizeName(oldPlaylistName);
            string newSafe = SanitizeName(newPlaylistName);

            string oldDir = Path.Combine(myMusic, oldSafe);
            string newDir = Path.Combine(myMusic, newSafe);

            if (Directory.Exists(oldDir) && !string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(newDir))
                {
                    // Si el destino ya existe, mover los archivos dentro
                    foreach (var file in Directory.GetFiles(oldDir))
                    {
                        string destFile = Path.Combine(newDir, Path.GetFileName(file));
                        File.Move(file, destFile, overwrite: true);
                    }
                    Directory.Delete(oldDir, recursive: true);
                }
                else
                {
                    Directory.Move(oldDir, newDir);
                }

                // Actualizar rutas de pistas en SQLite
                using var conn = await DatabaseContext.Instance.CreateConnectionAsync();
                using var cmd = conn.CreateCommand();
                string oldPrefix = oldDir + Path.DirectorySeparatorChar;
                string newPrefix = newDir + Path.DirectorySeparatorChar;

                cmd.CommandText = @"
                    UPDATE tracks
                    SET file_path = $newPrefix || SUBSTR(file_path, LENGTH($oldPrefix) + 1)
                    WHERE file_path LIKE $oldPattern;
                ";
                cmd.Parameters.AddWithValue("$newPrefix", newPrefix);
                cmd.Parameters.AddWithValue("$oldPrefix", oldPrefix);
                cmd.Parameters.AddWithValue("$oldPattern", oldPrefix + "%");
                await cmd.ExecuteNonQueryAsync();

                // Actualizar las instancias en memoria en la lista de reproducción abierta
                var currentDetail = ViewModels.MainViewModel.Instance?.SelectedPlaylistForDetail;
                if (currentDetail != null && currentDetail.Id == playlistId)
                {
                    foreach (var t in currentDetail.Tracks)
                    {
                        if (t.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            t.FilePath = newPrefix + t.FilePath.Substring(oldPrefix.Length);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractorOsu] Error renombrando carpeta de playlist: {ex.Message}");
        }
    }
}

/// <summary>
/// Alias alternativo según solicitud ("extractorgame o extractorosu")
/// </summary>
public static class ExtractorGame
{
    public static bool IsOsuTrack(Track? track) => ExtractorOsu.IsOsuTrack(track);

    public static Task<Track> ExtractTrackAsync(Track osuTrack, string playlistName, IProgress<string>? progress = null)
        => ExtractorOsu.ExtractTrackAsync(osuTrack, playlistName, progress);

    public static Task<List<Track>> ProcessAndExtractTracksAsync(IEnumerable<Track> tracks, string playlistName, IProgress<string>? progress = null)
        => ExtractorOsu.ProcessAndExtractTracksAsync(tracks, playlistName, progress);

    public static Task RenamePlaylistFolderAsync(string oldName, string newName, long playlistId)
        => ExtractorOsu.RenamePlaylistFolderAsync(oldName, newName, playlistId);
}
