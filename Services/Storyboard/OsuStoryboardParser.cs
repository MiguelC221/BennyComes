using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BenyComes.Services.Storyboard;

/// <summary>
/// Parser de Nivel 1 para Storyboards de osu! (.osb y sección [Events] de archivos .osu).
/// Soporta [Variables], Sprite, Animation, comandos principales (F, M, MX, MY, S, V, R, C, P aditivo/flip),
/// comandos encadenados y bucles L (Loop), ignorando triggers T de gameplay y la capa Fail.
/// </summary>
public static class OsuStoryboardParser
{
    private const int MaxUnrolledLoopIterations = 2048;
    private const int MaxSpritesPerStoryboard = 45000;

    /// <summary>
    /// Determina rápidamente si la carpeta de un beatmap contiene un storyboard (.osb o comandos Sprite/Animation en .osu).
    /// </summary>
    public static bool HasStoryboardInFolder(string? audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath)) return false;
        string? folder = Path.GetDirectoryName(audioFilePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;

        try
        {
            // 1. Si hay un archivo .osb con contenido relevante
            foreach (var osbFile in Directory.EnumerateFiles(folder, "*.osb"))
            {
                if (FileContainsStoryboardObjects(osbFile))
                    return true;
            }

            // 2. O si algún archivo .osu de la carpeta contiene objetos Sprite/Animation en [Events]
            string audioFileName = Path.GetFileName(audioFilePath);
            var osuFiles = Directory.EnumerateFiles(folder, "*.osu").ToList();
            foreach (var osuFile in osuFiles)
            {
                if (FileContainsStoryboardObjects(osuFile, audioFileName))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private static bool FileContainsStoryboardObjects(string filePath, string? expectedAudioFileName = null)
    {
        try
        {
            using var sr = new StreamReader(filePath, Encoding.UTF8);
            string? line;
            bool inEvents = false;
            bool inGeneral = false;

            while ((line = sr.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

                if (trimmed[0] == '[' && trimmed[^1] == ']')
                {
                    inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    inEvents = trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inGeneral && !string.IsNullOrEmpty(expectedAudioFileName) &&
                    trimmed.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed["AudioFilename:".Length..].Trim();
                    if (!string.IsNullOrEmpty(val) && !val.Equals(expectedAudioFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (inEvents && line[0] != ' ' && line[0] != '_')
                {
                    if (trimmed.StartsWith("Sprite,", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("Animation,", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("4,", StringComparison.Ordinal) ||
                        trimmed.StartsWith("6,", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public static OsuStoryboard? ParseFromTrackAudioPath(string? audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath)) return null;
        string? folder = Path.GetDirectoryName(audioFilePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        try
        {
            string audioFileName = Path.GetFileName(audioFilePath);
            var storyboard = new OsuStoryboard
            {
                FolderPath = folder,
                WidescreenStoryboard = false
            };

            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            var rawSprites = new List<StoryboardSprite>();

            // 1. Elegir el mejor archivo .osu de la carpeta (el que coincida con AudioFilename y tenga más líneas de eventos)
            string? bestOsuFile = null;
            int bestOsuEventScore = -1;
            bool anyOsuWidescreen = false;

            foreach (var osuFile in Directory.EnumerateFiles(folder, "*.osu"))
            {
                int score = ScoreOsuFileForStoryboard(osuFile, audioFileName, out bool isWidescreen);
                if (isWidescreen) anyOsuWidescreen = true;
                if (score > bestOsuEventScore)
                {
                    bestOsuEventScore = score;
                    bestOsuFile = osuFile;
                }
            }

            if (anyOsuWidescreen)
            {
                storyboard.WidescreenStoryboard = true;
            }

            if (bestOsuFile != null)
            {
                ParseSingleFile(bestOsuFile, storyboard, variables, rawSprites, isOsuFile: true);
            }

            // 2. Parsear archivo .osb (en osu!, el .osb complementa los eventos del .osu)
            var osbFile = Directory.EnumerateFiles(folder, "*.osb").FirstOrDefault();
            if (osbFile != null)
            {
                ParseSingleFile(osbFile, storyboard, variables, rawSprites, isOsuFile: false);
            }

            // 3. Finalizar líneas de tiempo, filtrar sprites vacíos o de la capa Fail y ordenar por capa
            var validSprites = new List<StoryboardSprite>(rawSprites.Count);
            double maxEndTime = double.MinValue;
            bool replacesBg = false;
            foreach (var sp in rawSprites)
            {
                if (sp.Layer == StoryboardLayer.Fail) continue;
                sp.FinalizeTimelines(folder);
                if (sp.HasCommands)
                {
                    validSprites.Add(sp);
                    if (sp.ActiveEndTime > maxEndTime)
                        maxEndTime = sp.ActiveEndTime;

                    if (!replacesBg)
                    {
                        if (sp.Layer == StoryboardLayer.Background ||
                            (!string.IsNullOrEmpty(storyboard.BeatmapBackgroundRelativePath) &&
                             string.Equals(sp.RelativeFilePath, storyboard.BeatmapBackgroundRelativePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            replacesBg = true;
                        }
                    }
                }
            }

            storyboard.MaxEndTimeMs = maxEndTime;
            storyboard.ReplacesBeatmapBackground = replacesBg;

            // Orden estable por capa: Background (0) -> Pass (2) -> Foreground (3) -> Overlay (4)
            foreach (var sp in validSprites.OrderBy(static s => (int)s.Layer))
            {
                storyboard.Sprites.Add(sp);
            }

            return storyboard.HasRenderableSprites ? storyboard : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OsuStoryboardParser] Error parsing storyboard: {ex.Message}");
            return null;
        }
    }

    private static int ScoreOsuFileForStoryboard(string osuFile, string expectedAudioFileName, out bool isWidescreen)
    {
        isWidescreen = false;
        try
        {
            using var sr = new StreamReader(osuFile, Encoding.UTF8);
            string? line;
            bool inGeneral = false;
            bool inEvents = false;
            int spriteCount = 0;

            while ((line = sr.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

                if (trimmed[0] == '[' && trimmed[^1] == ']')
                {
                    inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    inEvents = trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inGeneral)
                {
                    if (trimmed.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = trimmed["AudioFilename:".Length..].Trim();
                        if (!string.IsNullOrEmpty(val) && !val.Equals(expectedAudioFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            isWidescreen = false;
                            return -1;
                        }
                    }
                    else if (trimmed.StartsWith("WidescreenStoryboard:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = trimmed["WidescreenStoryboard:".Length..].Trim();
                        if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            isWidescreen = true;
                        }
                    }
                }
                else if (inEvents && line[0] != ' ' && line[0] != '_')
                {
                    if (trimmed.StartsWith("Sprite,", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("Animation,", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("4,", StringComparison.Ordinal) ||
                        trimmed.StartsWith("6,", StringComparison.Ordinal))
                    {
                        spriteCount++;
                    }
                }
            }
            return spriteCount;
        }
        catch
        {
            return -1;
        }
    }

    private sealed class LoopBlock
    {
        public double StartTime { get; init; }
        public int LoopCount { get; init; }
        public List<string> RawCommandLines { get; } = new();
    }

    private static void ParseSingleFile(
        string filePath,
        OsuStoryboard storyboard,
        Dictionary<string, string> variables,
        List<StoryboardSprite> sprites,
        bool isOsuFile)
    {
        using var sr = new StreamReader(filePath, Encoding.UTF8);
        string? rawLine;
        string currentSection = string.Empty;

        StoryboardSprite? currentSprite = null;
        LoopBlock? currentLoop = null;
        bool inTriggerBlock = false;
        int blockHeaderDepth = int.MaxValue;

        void FlushCurrentLoop()
        {
            if (currentLoop != null && currentSprite != null)
            {
                UnrollLoopIntoSprite(currentSprite, currentLoop);
            }
            currentLoop = null;
            blockHeaderDepth = int.MaxValue;
        }

        while ((rawLine = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("//")) continue;

            if (trimmed[0] == '[' && trimmed[^1] == ']')
            {
                FlushCurrentLoop();
                inTriggerBlock = false;
                currentSection = trimmed;
                continue;
            }

            if (currentSection.Equals("[General]", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.StartsWith("WidescreenStoryboard:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed["WidescreenStoryboard:".Length..].Trim();
                    if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        storyboard.WidescreenStoryboard = true;
                }
                continue;
            }

            if (currentSection.Equals("[Variables]", StringComparison.OrdinalIgnoreCase))
            {
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = trimmed[..eqIdx].Trim();
                    string val = trimmed[(eqIdx + 1)..].Trim();
                    variables[key] = val;
                }
                continue;
            }

            if (!currentSection.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
                continue;

            // Reemplazar variables ($var) si existen
            string line = rawLine;
            if (variables.Count > 0 && line.Contains('$'))
            {
                foreach (var kv in variables)
                {
                    if (line.Contains(kv.Key, StringComparison.Ordinal))
                        line = line.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
                }
            }

            int depth = GetIndentDepth(line);
            string content = line.AsSpan(depth).Trim().ToString();
            if (string.IsNullOrEmpty(content) || content.StartsWith("//")) continue;

            if (depth == 0)
            {
                FlushCurrentLoop();
                inTriggerBlock = false;

                string[] parts = SplitCsvLine(content);
                if (parts.Length == 0) continue;

                string typeToken = parts[0];

                // Evento 0 / Background del beatmap
                if ((typeToken == "0" || typeToken.Equals("Background", StringComparison.OrdinalIgnoreCase)) && parts.Length >= 3)
                {
                    if (isOsuFile && string.IsNullOrEmpty(storyboard.BeatmapBackgroundRelativePath))
                    {
                        storyboard.BeatmapBackgroundRelativePath = NormalizeRelativePath(parts[2]);
                    }
                    currentSprite = null;
                    continue;
                }

                // Sprite: Sprite,layer,origin,"filepath",x,y
                if ((typeToken.Equals("Sprite", StringComparison.OrdinalIgnoreCase) || typeToken == "4") && parts.Length >= 6)
                {
                    if (sprites.Count >= MaxSpritesPerStoryboard)
                    {
                        currentSprite = null;
                        continue;
                    }

                    var layer = ParseLayer(parts[1]);
                    var origin = ParseOrigin(parts[2]);
                    string relPath = NormalizeRelativePath(parts[3]);
                    float x = ParseFloat(parts[4], 320f);
                    float y = ParseFloat(parts[5], 240f);

                    currentSprite = new StoryboardSprite
                    {
                        Layer = layer,
                        Origin = origin,
                        RelativeFilePath = relPath,
                        InitialX = x,
                        InitialY = y,
                        IsAnimation = false
                    };
                    sprites.Add(currentSprite);
                }
                // Animation: Animation,layer,origin,"filepath",x,y,frameCount,frameDelay,loopType
                else if ((typeToken.Equals("Animation", StringComparison.OrdinalIgnoreCase) || typeToken == "6") && parts.Length >= 8)
                {
                    if (sprites.Count >= MaxSpritesPerStoryboard)
                    {
                        currentSprite = null;
                        continue;
                    }

                    var layer = ParseLayer(parts[1]);
                    var origin = ParseOrigin(parts[2]);
                    string relPath = NormalizeRelativePath(parts[3]);
                    float x = ParseFloat(parts[4], 320f);
                    float y = ParseFloat(parts[5], 240f);
                    int frameCount = Math.Max(1, (int)ParseFloat(parts[6], 1f));
                    double frameDelay = Math.Max(10.0, ParseDouble(parts[7], 60.0));
                    var loopType = parts.Length >= 9 && parts[8].Equals("LoopOnce", StringComparison.OrdinalIgnoreCase)
                        ? StoryboardLoopType.LoopOnce
                        : StoryboardLoopType.LoopForever;

                    currentSprite = new StoryboardSprite
                    {
                        Layer = layer,
                        Origin = origin,
                        RelativeFilePath = relPath,
                        InitialX = x,
                        InitialY = y,
                        IsAnimation = true,
                        FrameCount = frameCount,
                        FrameDelayMs = frameDelay,
                        LoopType = loopType
                    };
                    sprites.Add(currentSprite);
                }
                else
                {
                    currentSprite = null;
                }
            }
            else
            {
                if (currentSprite == null) continue;

                // Si salimos de la indentación del bloque L o T actual, cerrar el bloque
                if ((currentLoop != null || inTriggerBlock) && depth <= blockHeaderDepth)
                {
                    FlushCurrentLoop();
                    inTriggerBlock = false;
                }

                // Si estamos dentro de un bloque L o T (depth > blockHeaderDepth)
                if (currentLoop != null || inTriggerBlock)
                {
                    if (!inTriggerBlock && currentLoop != null)
                    {
                        currentLoop.RawCommandLines.Add(content);
                    }
                    continue;
                }

                // Loop: L,startTime,loopCount
                if (content.StartsWith("L,", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = content.Split(',');
                    if (parts.Length >= 3)
                    {
                        double startTime = ParseDouble(parts[1], 0.0);
                        int loopCount = Math.Clamp((int)ParseFloat(parts[2], 1f), 1, MaxUnrolledLoopIterations);
                        currentLoop = new LoopBlock
                        {
                            StartTime = startTime,
                            LoopCount = loopCount
                        };
                        blockHeaderDepth = depth;
                    }
                    continue;
                }

                // Trigger: T,triggerName,startTime,endTime -> Ignorado en Nivel 1
                if (content.StartsWith("T,", StringComparison.OrdinalIgnoreCase))
                {
                    inTriggerBlock = true;
                    blockHeaderDepth = depth;
                    continue;
                }

                ParseAndAddCommandLine(currentSprite, content, timeOffset: 0.0);
            }
        }

        FlushCurrentLoop();
    }

    private static void UnrollLoopIntoSprite(StoryboardSprite sprite, LoopBlock loop)
    {
        if (loop.RawCommandLines.Count == 0 || loop.LoopCount <= 0) return;

        // Calcular la duración de una iteración del bucle buscando el máximo endTime local
        double minLocalStart = double.MaxValue;
        double maxLocalEnd = double.MinValue;

        foreach (var cmdLine in loop.RawCommandLines)
        {
            string[] parts = cmdLine.Split(',');
            if (parts.Length >= 4)
            {
                double s = ParseDouble(parts[2], 0.0);
                double e = string.IsNullOrWhiteSpace(parts[3]) ? s : ParseDouble(parts[3], s);
                if (s < minLocalStart) minLocalStart = s;
                if (e > maxLocalEnd) maxLocalEnd = e;
            }
        }

        if (minLocalStart > maxLocalEnd) return;
        double loopDuration = Math.Max(0.0, maxLocalEnd);
        if (loopDuration <= 0.0)
        {
            // Si la duración es 0, ejecutar solo 1 vez
            foreach (var cmdLine in loop.RawCommandLines)
            {
                ParseAndAddCommandLine(sprite, cmdLine, loop.StartTime);
            }
            return;
        }

        int iterations = Math.Min(loop.LoopCount, MaxUnrolledLoopIterations);
        for (int i = 0; i < iterations; i++)
        {
            double offset = loop.StartTime + (i * loopDuration);
            foreach (var cmdLine in loop.RawCommandLines)
            {
                ParseAndAddCommandLine(sprite, cmdLine, offset);
            }
        }
    }

    private static void ParseAndAddCommandLine(StoryboardSprite sprite, string content, double timeOffset)
    {
        string[] parts = content.Split(',');
        if (parts.Length < 4) return;

        string cmdType = parts[0].Trim().ToUpperInvariant();
        int easing = (int)ParseFloat(parts[1], 0f);
        double baseStartTime = ParseDouble(parts[2], 0.0) + timeOffset;
        double baseEndTime = (string.IsNullOrWhiteSpace(parts[3]) ? ParseDouble(parts[2], 0.0) : ParseDouble(parts[3], 0.0)) + timeOffset;
        if (baseEndTime < baseStartTime) baseEndTime = baseStartTime;
        double duration = baseEndTime - baseStartTime;

        switch (cmdType)
        {
            case "F": // Fade: F,easing,start,end,startVal[,endVal...]
            case "S": // Scale: S,easing,start,end,startVal[,endVal...]
            case "R": // Rotate: R,easing,start,end,startVal[,endVal...]
            case "MX": // MoveX: MX,easing,start,end,startVal[,endVal...]
            case "MY": // MoveY: MY,easing,start,end,startVal[,endVal...]
            {
                if (parts.Length < 5) break;
                var targetList = cmdType switch
                {
                    "F" => sprite.FadeCommands,
                    "S" => sprite.ScaleCommands,
                    "R" => sprite.RotateCommands,
                    "MX" => sprite.MoveXCommands,
                    _ => sprite.MoveYCommands
                };

                float v0 = ParseFloat(parts[4], 0f);
                if (parts.Length == 5)
                {
                    targetList.Add(new ScalarCommand(easing, baseStartTime, baseEndTime, v0, v0, sprite.NextOrderIndex()));
                }
                else
                {
                    // Soporta secuencias encadenadas: F,e,s,e,v0,v1,v2...
                    int segIndex = 0;
                    for (int idx = 4; idx < parts.Length - 1; idx++)
                    {
                        float sv = ParseFloat(parts[idx], v0);
                        float ev = ParseFloat(parts[idx + 1], sv);
                        double st = baseStartTime + (segIndex * duration);
                        double et = st + duration;
                        targetList.Add(new ScalarCommand(easing, st, et, sv, ev, sprite.NextOrderIndex()));
                        segIndex++;
                    }
                }
                break;
            }

            case "M": // Move XY: M,easing,start,end,startX,startY[,endX,endY...]
            case "V": // Vector Scale XY: V,easing,start,end,startX,startY[,endX,endY...]
            {
                if (parts.Length < 6) break;
                var targetList = cmdType == "M" ? sprite.MoveCommands : sprite.VectorScaleCommands;

                float x0 = ParseFloat(parts[4], 0f);
                float y0 = ParseFloat(parts[5], 0f);
                if (parts.Length < 8)
                {
                    targetList.Add(new Vector2Command(easing, baseStartTime, baseEndTime, x0, y0, x0, y0, sprite.NextOrderIndex()));
                }
                else
                {
                    int segIndex = 0;
                    for (int idx = 4; idx <= parts.Length - 4; idx += 2)
                    {
                        float sx = ParseFloat(parts[idx], x0);
                        float sy = ParseFloat(parts[idx + 1], y0);
                        float ex = ParseFloat(parts[idx + 2], sx);
                        float ey = ParseFloat(parts[idx + 3], sy);
                        double st = baseStartTime + (segIndex * duration);
                        double et = st + duration;
                        targetList.Add(new Vector2Command(easing, st, et, sx, sy, ex, ey, sprite.NextOrderIndex()));
                        segIndex++;
                    }
                }
                break;
            }

            case "C": // Color RGB: C,easing,start,end,r1,g1,b1[,r2,g2,b2...]
            {
                if (parts.Length < 7) break;
                float r0 = ParseFloat(parts[4], 255f);
                float g0 = ParseFloat(parts[5], 255f);
                float b0 = ParseFloat(parts[6], 255f);
                if (parts.Length < 10)
                {
                    sprite.ColorCommands.Add(new ColorCommand(easing, baseStartTime, baseEndTime, r0, g0, b0, r0, g0, b0, sprite.NextOrderIndex()));
                }
                else
                {
                    int segIndex = 0;
                    for (int idx = 4; idx <= parts.Length - 6; idx += 3)
                    {
                        float sr = ParseFloat(parts[idx], 255f);
                        float sg = ParseFloat(parts[idx + 1], 255f);
                        float sb = ParseFloat(parts[idx + 2], 255f);
                        float er = ParseFloat(parts[idx + 3], sr);
                        float eg = ParseFloat(parts[idx + 4], sg);
                        float eb = ParseFloat(parts[idx + 5], sb);
                        double st = baseStartTime + (segIndex * duration);
                        double et = st + duration;
                        sprite.ColorCommands.Add(new ColorCommand(easing, st, et, sr, sg, sb, er, eg, eb, sprite.NextOrderIndex()));
                        segIndex++;
                    }
                }
                break;
            }

            case "P": // Parameter: P,easing,start,end,param (A, H, V)
            {
                if (parts.Length < 5) break;
                string pStr = parts[4].Trim();
                if (!string.IsNullOrEmpty(pStr))
                {
                    char pChar = char.ToUpperInvariant(pStr[0]);
                    if (pChar is 'A' or 'H' or 'V')
                    {
                        sprite.ParameterCommands.Add(new ParameterCommand(baseStartTime, baseEndTime, pChar, sprite.NextOrderIndex()));
                    }
                }
                break;
            }
        }
    }

    private static int GetIndentDepth(string line)
    {
        int depth = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ' ' || c == '_' || c == '\t')
                depth++;
            else
                break;
        }
        return depth;
    }

    private static string NormalizeRelativePath(string raw)
    {
        string cleaned = raw.Trim().Trim('"');
        cleaned = cleaned.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        while (cleaned.StartsWith(Path.DirectorySeparatorChar))
            cleaned = cleaned[1..];
        return cleaned;
    }

    private static StoryboardLayer ParseLayer(string token)
    {
        token = token.Trim();
        return token switch
        {
            "0" or "Background" or "background" => StoryboardLayer.Background,
            "1" or "Fail" or "fail" => StoryboardLayer.Fail,
            "2" or "Pass" or "pass" => StoryboardLayer.Pass,
            "3" or "Foreground" or "foreground" => StoryboardLayer.Foreground,
            "4" or "Overlay" or "overlay" => StoryboardLayer.Overlay,
            _ => StoryboardLayer.Background
        };
    }

    private static StoryboardOrigin ParseOrigin(string token)
    {
        token = token.Trim();
        return token switch
        {
            "0" or "TopLeft" => StoryboardOrigin.TopLeft,
            "1" or "Centre" or "Center" => StoryboardOrigin.Centre,
            "2" or "CentreLeft" or "CenterLeft" => StoryboardOrigin.CentreLeft,
            "3" or "TopRight" => StoryboardOrigin.TopRight,
            "4" or "BottomCentre" or "BottomCenter" => StoryboardOrigin.BottomCentre,
            "5" or "TopCentre" or "TopCenter" => StoryboardOrigin.TopCentre,
            "6" or "Custom" => StoryboardOrigin.Centre,
            "7" or "CentreRight" or "CenterRight" => StoryboardOrigin.CentreRight,
            "8" or "BottomLeft" => StoryboardOrigin.BottomLeft,
            "9" or "BottomRight" => StoryboardOrigin.BottomRight,
            _ => StoryboardOrigin.Centre
        };
    }

    private static string[] SplitCsvLine(string line)
    {
        if (!line.Contains('"'))
            return line.Split(',');

        var result = new List<string>(8);
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim());
        return result.ToArray();
    }

    private static float ParseFloat(string s, float fallback)
    {
        return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    private static double ParseDouble(string s, double fallback)
    {
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }
}
