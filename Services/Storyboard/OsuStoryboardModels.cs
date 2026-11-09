using System;
using System.Collections.Generic;
using System.IO;

namespace BenyComes.Services.Storyboard;

public enum StoryboardLayer
{
    Background = 0,
    Fail = 1,
    Pass = 2,
    Foreground = 3,
    Overlay = 4
}

public enum StoryboardOrigin
{
    TopLeft = 0,
    Centre = 1,
    CentreLeft = 2,
    TopRight = 3,
    BottomCentre = 4,
    TopCentre = 5,
    Custom = 6,
    CentreRight = 7,
    BottomLeft = 8,
    BottomRight = 9
}

public enum StoryboardLoopType
{
    LoopForever = 0,
    LoopOnce = 1
}

public readonly struct ScalarCommand
{
    public readonly int Easing;
    public readonly double StartTime;
    public readonly double EndTime;
    public readonly float StartValue;
    public readonly float EndValue;

    public ScalarCommand(int easing, double startTime, double endTime, float startVal, float endVal)
    {
        Easing = easing;
        StartTime = startTime;
        EndTime = endTime < startTime ? startTime : endTime;
        StartValue = startVal;
        EndValue = endVal;
    }

    public float Interpolate(double time)
    {
        if (time <= StartTime || EndTime <= StartTime) return StartValue;
        if (time >= EndTime) return EndValue;
        double progress = (time - StartTime) / (EndTime - StartTime);
        float eased = OsuStoryboardEasing.Apply(Easing, progress);
        return StartValue + (EndValue - StartValue) * eased;
    }
}

public readonly struct Vector2Command
{
    public readonly int Easing;
    public readonly double StartTime;
    public readonly double EndTime;
    public readonly float StartX;
    public readonly float StartY;
    public readonly float EndX;
    public readonly float EndY;

    public Vector2Command(int easing, double startTime, double endTime, float sx, float sy, float ex, float ey)
    {
        Easing = easing;
        StartTime = startTime;
        EndTime = endTime < startTime ? startTime : endTime;
        StartX = sx;
        StartY = sy;
        EndX = ex;
        EndY = ey;
    }

    public (float X, float Y) Interpolate(double time)
    {
        if (time <= StartTime || EndTime <= StartTime) return (StartX, StartY);
        if (time >= EndTime) return (EndX, EndY);
        double progress = (time - StartTime) / (EndTime - StartTime);
        float eased = OsuStoryboardEasing.Apply(Easing, progress);
        return (StartX + (EndX - StartX) * eased, StartY + (EndY - StartY) * eased);
    }
}

public readonly struct ColorCommand
{
    public readonly int Easing;
    public readonly double StartTime;
    public readonly double EndTime;
    public readonly float StartR;
    public readonly float StartG;
    public readonly float StartB;
    public readonly float EndR;
    public readonly float EndG;
    public readonly float EndB;

    public ColorCommand(int easing, double startTime, double endTime, float sr, float sg, float sb, float er, float eg, float eb)
    {
        Easing = easing;
        StartTime = startTime;
        EndTime = endTime < startTime ? startTime : endTime;
        StartR = sr;
        StartG = sg;
        StartB = sb;
        EndR = er;
        EndG = eg;
        EndB = eb;
    }

    public (byte R, byte G, byte B) Interpolate(double time)
    {
        if (time <= StartTime || EndTime <= StartTime)
            return ((byte)Math.Clamp(StartR, 0f, 255f), (byte)Math.Clamp(StartG, 0f, 255f), (byte)Math.Clamp(StartB, 0f, 255f));
        if (time >= EndTime)
            return ((byte)Math.Clamp(EndR, 0f, 255f), (byte)Math.Clamp(EndG, 0f, 255f), (byte)Math.Clamp(EndB, 0f, 255f));

        double progress = (time - StartTime) / (EndTime - StartTime);
        float eased = OsuStoryboardEasing.Apply(Easing, progress);
        float r = StartR + (EndR - StartR) * eased;
        float g = StartG + (EndG - StartG) * eased;
        float b = StartB + (EndB - StartB) * eased;
        return ((byte)Math.Clamp(r, 0f, 255f), (byte)Math.Clamp(g, 0f, 255f), (byte)Math.Clamp(b, 0f, 255f));
    }
}

public readonly struct ParameterCommand
{
    public readonly double StartTime;
    public readonly double EndTime;
    public readonly char ParameterType; // 'A' = Additive, 'H' = FlipH, 'V' = FlipV

    public ParameterCommand(double startTime, double endTime, char paramType)
    {
        StartTime = startTime;
        EndTime = endTime < startTime ? startTime : endTime;
        ParameterType = paramType;
    }

    public bool IsActiveAt(double time)
    {
        if (StartTime == EndTime)
            return time >= StartTime;
        return time >= StartTime && time <= EndTime;
    }
}

public struct SpriteRenderState
{
    public bool IsVisible;
    public float X;
    public float Y;
    public float ScaleX;
    public float ScaleY;
    public float RotationRad;
    public float Opacity;
    public byte R;
    public byte G;
    public byte B;
    public bool Additive;
    public bool FlipH;
    public bool FlipV;
    public string TextureRelativePath;
}

public sealed class StoryboardSprite
{
    public StoryboardLayer Layer { get; init; }
    public StoryboardOrigin Origin { get; init; }
    public string RelativeFilePath { get; init; } = string.Empty;
    public float InitialX { get; init; }
    public float InitialY { get; init; }

    // Soporte de Animation (secuencias de fotogramas)
    public bool IsAnimation { get; init; }
    public int FrameCount { get; init; }
    public double FrameDelayMs { get; init; }
    public StoryboardLoopType LoopType { get; init; }
    public string[]? AnimationFramePaths { get; set; }

    public List<ScalarCommand> FadeCommands { get; } = new();
    public List<Vector2Command> MoveCommands { get; } = new();
    public List<ScalarCommand> MoveXCommands { get; } = new();
    public List<ScalarCommand> MoveYCommands { get; } = new();
    public List<ScalarCommand> ScaleCommands { get; } = new();
    public List<Vector2Command> VectorScaleCommands { get; } = new();
    public List<ScalarCommand> RotateCommands { get; } = new();
    public List<ColorCommand> ColorCommands { get; } = new();
    public List<ParameterCommand> ParameterCommands { get; } = new();

    public double ActiveStartTime { get; private set; } = double.MaxValue;
    public double ActiveEndTime { get; private set; } = double.MinValue;
    public bool HasCommands { get; private set; }

    public void FinalizeTimelines()
    {
        FadeCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        MoveCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        MoveXCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        MoveYCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        ScaleCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        VectorScaleCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        RotateCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        ColorCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        ParameterCommands.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));

        double minT = double.MaxValue;
        double maxT = double.MinValue;

        UpdateBounds(FadeCommands, ref minT, ref maxT);
        UpdateBounds(MoveCommands, ref minT, ref maxT);
        UpdateBounds(MoveXCommands, ref minT, ref maxT);
        UpdateBounds(MoveYCommands, ref minT, ref maxT);
        UpdateBounds(ScaleCommands, ref minT, ref maxT);
        UpdateBounds(VectorScaleCommands, ref minT, ref maxT);
        UpdateBounds(RotateCommands, ref minT, ref maxT);
        UpdateBounds(ColorCommands, ref minT, ref maxT);
        foreach (var p in ParameterCommands)
        {
            if (p.StartTime < minT) minT = p.StartTime;
            if (p.EndTime > maxT) maxT = p.EndTime;
        }

        if (minT <= maxT)
        {
            ActiveStartTime = minT;
            ActiveEndTime = maxT;
            HasCommands = true;
        }
        else
        {
            HasCommands = false;
        }

        if (IsAnimation && FrameCount > 0 && !string.IsNullOrEmpty(RelativeFilePath))
        {
            string ext = Path.GetExtension(RelativeFilePath);
            string baseNoExt = RelativeFilePath[..^ext.Length];
            var paths = new string[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                paths[i] = $"{baseNoExt}{i}{ext}";
            }
            AnimationFramePaths = paths;
        }
    }

    private static void UpdateBounds(List<ScalarCommand> list, ref double minT, ref double maxT)
    {
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.StartTime < minT) minT = c.StartTime;
            if (c.EndTime > maxT) maxT = c.EndTime;
        }
    }

    private static void UpdateBounds(List<Vector2Command> list, ref double minT, ref double maxT)
    {
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.StartTime < minT) minT = c.StartTime;
            if (c.EndTime > maxT) maxT = c.EndTime;
        }
    }

    private static void UpdateBounds(List<ColorCommand> list, ref double minT, ref double maxT)
    {
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.StartTime < minT) minT = c.StartTime;
            if (c.EndTime > maxT) maxT = c.EndTime;
        }
    }

    public SpriteRenderState Evaluate(double timeMs)
    {
        var state = new SpriteRenderState
        {
            IsVisible = false,
            X = InitialX,
            Y = InitialY,
            ScaleX = 1f,
            ScaleY = 1f,
            RotationRad = 0f,
            Opacity = 1f,
            R = 255,
            G = 255,
            B = 255,
            Additive = false,
            FlipH = false,
            FlipV = false,
            TextureRelativePath = RelativeFilePath
        };

        if (!HasCommands || timeMs < ActiveStartTime || timeMs > ActiveEndTime)
            return state;

        // 1. Opacity (Fade)
        if (FadeCommands.Count > 0)
        {
            state.Opacity = EvaluateScalar(FadeCommands, timeMs, 1f);
        }
        if (state.Opacity <= 0.002f)
            return state;

        // 2. Scale / VectorScale
        bool hasScale = ScaleCommands.Count > 0;
        bool hasVecScale = VectorScaleCommands.Count > 0;
        if (hasScale && hasVecScale)
        {
            // Usar el comando más reciente entre S y V
            double lastS = GetActiveCommandTime(ScaleCommands, timeMs);
            double lastV = GetActiveCommandTime(VectorScaleCommands, timeMs);
            if (lastV >= lastS)
            {
                var (sx, sy) = EvaluateVector2(VectorScaleCommands, timeMs, 1f, 1f);
                state.ScaleX = sx;
                state.ScaleY = sy;
            }
            else
            {
                float s = EvaluateScalar(ScaleCommands, timeMs, 1f);
                state.ScaleX = s;
                state.ScaleY = s;
            }
        }
        else if (hasVecScale)
        {
            var (sx, sy) = EvaluateVector2(VectorScaleCommands, timeMs, 1f, 1f);
            state.ScaleX = sx;
            state.ScaleY = sy;
        }
        else if (hasScale)
        {
            float s = EvaluateScalar(ScaleCommands, timeMs, 1f);
            state.ScaleX = s;
            state.ScaleY = s;
        }

        if (Math.Abs(state.ScaleX) <= 0.0005f || Math.Abs(state.ScaleY) <= 0.0005f)
            return state;

        // 3. Position (M vs MX/MY)
        if (MoveCommands.Count > 0)
        {
            var (mx, my) = EvaluateVector2(MoveCommands, timeMs, InitialX, InitialY);
            state.X = mx;
            state.Y = my;
        }
        if (MoveXCommands.Count > 0)
        {
            if (MoveCommands.Count == 0 || GetActiveCommandTime(MoveXCommands, timeMs) >= GetActiveCommandTime(MoveCommands, timeMs))
            {
                state.X = EvaluateScalar(MoveXCommands, timeMs, state.X);
            }
        }
        if (MoveYCommands.Count > 0)
        {
            if (MoveCommands.Count == 0 || GetActiveCommandTime(MoveYCommands, timeMs) >= GetActiveCommandTime(MoveCommands, timeMs))
            {
                state.Y = EvaluateScalar(MoveYCommands, timeMs, state.Y);
            }
        }

        // 4. Rotation
        if (RotateCommands.Count > 0)
        {
            state.RotationRad = EvaluateScalar(RotateCommands, timeMs, 0f);
        }

        // 5. Color
        if (ColorCommands.Count > 0)
        {
            var (r, g, b) = EvaluateColor(ColorCommands, timeMs);
            state.R = r;
            state.G = g;
            state.B = b;
        }

        // 6. Parameters (Additive, FlipH, FlipV)
        for (int i = 0; i < ParameterCommands.Count; i++)
        {
            var p = ParameterCommands[i];
            if (p.IsActiveAt(timeMs))
            {
                switch (p.ParameterType)
                {
                    case 'A':
                        state.Additive = true;
                        break;
                    case 'H':
                        state.FlipH = true;
                        break;
                    case 'V':
                        state.FlipV = true;
                        break;
                }
            }
        }

        // 7. Animation frame selection
        if (IsAnimation && AnimationFramePaths != null && AnimationFramePaths.Length > 0 && FrameDelayMs > 0)
        {
            double elapsed = Math.Max(0.0, timeMs - ActiveStartTime);
            int frameIdx = (int)(elapsed / FrameDelayMs);
            if (LoopType == StoryboardLoopType.LoopOnce)
            {
                frameIdx = Math.Clamp(frameIdx, 0, AnimationFramePaths.Length - 1);
            }
            else
            {
                frameIdx %= AnimationFramePaths.Length;
            }
            state.TextureRelativePath = AnimationFramePaths[frameIdx];
        }

        state.IsVisible = true;
        return state;
    }

    private static double GetActiveCommandTime(List<ScalarCommand> cmds, double time)
    {
        if (cmds.Count == 0) return double.MinValue;
        double t = cmds[0].StartTime;
        for (int i = 0; i < cmds.Count; i++)
        {
            if (cmds[i].StartTime <= time)
                t = cmds[i].StartTime;
            else
                break;
        }
        return t;
    }

    private static double GetActiveCommandTime(List<Vector2Command> cmds, double time)
    {
        if (cmds.Count == 0) return double.MinValue;
        double t = cmds[0].StartTime;
        for (int i = 0; i < cmds.Count; i++)
        {
            if (cmds[i].StartTime <= time)
                t = cmds[i].StartTime;
            else
                break;
        }
        return t;
    }

    private static float EvaluateScalar(List<ScalarCommand> cmds, double time, float defaultVal)
    {
        if (cmds.Count == 0) return defaultVal;
        if (time <= cmds[0].StartTime) return cmds[0].StartValue;

        ScalarCommand active = cmds[0];
        for (int i = 0; i < cmds.Count; i++)
        {
            var c = cmds[i];
            if (time >= c.StartTime)
            {
                active = c;
            }
            else
            {
                break;
            }
        }
        return active.Interpolate(time);
    }

    private static (float X, float Y) EvaluateVector2(List<Vector2Command> cmds, double time, float defX, float defY)
    {
        if (cmds.Count == 0) return (defX, defY);
        if (time <= cmds[0].StartTime) return (cmds[0].StartX, cmds[0].StartY);

        Vector2Command active = cmds[0];
        for (int i = 0; i < cmds.Count; i++)
        {
            var c = cmds[i];
            if (time >= c.StartTime)
            {
                active = c;
            }
            else
            {
                break;
            }
        }
        return active.Interpolate(time);
    }

    private static (byte R, byte G, byte B) EvaluateColor(List<ColorCommand> cmds, double time)
    {
        if (cmds.Count == 0) return (255, 255, 255);
        if (time <= cmds[0].StartTime)
        {
            var first = cmds[0];
            return ((byte)Math.Clamp(first.StartR, 0f, 255f),
                    (byte)Math.Clamp(first.StartG, 0f, 255f),
                    (byte)Math.Clamp(first.StartB, 0f, 255f));
        }

        ColorCommand active = cmds[0];
        for (int i = 0; i < cmds.Count; i++)
        {
            var c = cmds[i];
            if (time >= c.StartTime)
            {
                active = c;
            }
            else
            {
                break;
            }
        }
        return active.Interpolate(time);
    }
}

public sealed class OsuStoryboard
{
    public string FolderPath { get; init; } = string.Empty;
    public bool WidescreenStoryboard { get; set; } = true;
    public string? BeatmapBackgroundRelativePath { get; set; }
    public List<StoryboardSprite> Sprites { get; } = new();

    public bool HasRenderableSprites => Sprites.Count > 0;
}
