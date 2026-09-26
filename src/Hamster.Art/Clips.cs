namespace Hamster.Art;

/// <summary>
/// Un clip d'animation. LeavesBaseline marque les clips qui quittent volontairement
/// la ligne de base (saut, celebration, FX) : sans ca le checker les refuse a tort.
/// </summary>
public sealed record Clip(
    string Name,
    byte[][] Frames,
    int Fps,
    bool Loop,
    bool LeavesBaseline)
{
    public int FrameCount => Frames.Length;
    public double DurationSeconds => Frames.Length / (double)Fps;
}

/// <summary>Bibliotheque procedurale. Les PNG de la phase 3 remplaceront ces frames sans toucher au code appelant.</summary>
public static class Clips
{
    public const string Idle  = "idle";
    public const string Walk  = "walk";
    public const string Blink = "blink";
    public const string React = "react";

    public static Dictionary<string, Clip> BuildAll(int bowHue = 0)
    {
        var all = new[] { BuildIdle(bowHue), BuildWalk(bowHue), BuildBlink(bowHue), BuildReact(bowHue) };
        return all.ToDictionary(c => c.Name);
    }

    static Clip BuildIdle(int hue)
    {
        int[] bob = { 0, 0, -1, -1, -1, 0 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < bob.Length; i++)
        {
            var p = Pose.Default;
            p.BowHue = hue;
            p.BodyDy = bob[i];
            p.Squash = bob[i] == 0 ? 0 : -1;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Idle, frames, 5, true, false);
    }

    static Clip BuildWalk(int hue)
    {
        int[] bob  = { 0, -1, -1, 0, -1, -1 };
        int[] legs = { 1, 1, 0, 2, 2, 0 };
        int[] lean = { 1, 1, 0, -1, -1, 0 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < bob.Length; i++)
        {
            var p = Pose.Default;
            p.BowHue = hue;
            p.BodyDy = bob[i];
            p.LegPhase = legs[i];
            p.HeadDx = lean[i];
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Walk, frames, 9, true, false);
    }

    static Clip BuildBlink(int hue)
    {
        var frames = new byte[3][];
        for (int i = 0; i < 3; i++)
        {
            var p = Pose.Default;
            p.BowHue = hue;
            p.Blink = i == 1;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Blink, frames, 12, false, false);
    }

    static Clip BuildReact(int hue)
    {
        //                 anticipation      saut                retombee
        int[] jump  = { 0,  2,  2, -6, -11, -13, -11, -6,  0,  2,  0 };
        int[] squash = { 0, 2,  2, -1,  -1,   0,   0, -1,  2,  1,  0 };
        var frames = new byte[jump.Length][];
        for (int i = 0; i < jump.Length; i++)
        {
            var p = Pose.Default;
            p.BowHue = hue;
            p.BodyDy = jump[i];
            p.Squash = squash[i];
            p.Heart = i >= 3;
            p.HeartDy = i >= 3 ? -(i - 3) * 3 : 0;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(React, frames, 12, false, true);
    }
}
