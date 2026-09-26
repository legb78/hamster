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
public static partial class Clips
{
    public const string Idle  = "idle";
    public const string Walk  = "walk";
    public const string Blink = "blink";
    public const string React = "react";

    // ---- travail (Working) --------------------------------------------------
    public const string WorkLaptop   = "work_laptop";
    public const string WorkBook     = "work_book";
    public const string WorkTerminal = "work_terminal";
    public const string WorkLab      = "work_lab";
    public const string WorkConductor = "work_conductor";
    public const string Think        = "think";

    // ---- humeurs --------------------------------------------------------------
    /// <summary>WaitingUser, en boucle.</summary>
    public const string Phone     = "phone";
    /// <summary>One-shot d'environ 3 s, quitte la ligne de base.</summary>
    public const string Celebrate = "celebrate";
    /// <summary>One-shot d'environ 2 s.</summary>
    public const string Error     = "error";

    // ---- variantes de repos -----------------------------------------------------
    public const string IdleConsole = "idle_console";
    public const string IdleSnack   = "idle_snack";
    public const string IdleSleep   = "idle_sleep";
    public const string IdleStretch = "idle_stretch";
    /// <summary>Skate : la variante rare.</summary>
    public const string IdleWheel   = "idle_wheel";

    // ---- FX des minis : one-shots sans personnage, meme canvas 128 ---------------
    /// <summary>Nuage de fumee a l'apparition d'un mini.</summary>
    public const string FxPop     = "fx_pop";
    /// <summary>Etincelles a la disparition d'un mini.</summary>
    public const string FxSparkle = "fx_sparkle";

    /// <summary>Tirage des clips de travail, poids du contrat [B].</summary>
    public static readonly IReadOnlyList<(string Name, int Weight)> WorkPool = new[]
    {
        (WorkLaptop, 25), (WorkBook, 20), (WorkTerminal, 20), (WorkLab, 15), (WorkConductor, 15), (Think, 5),
    };

    /// <summary>Variantes de repos. Poids proposes ici : le contrat ne les fixe pas.</summary>
    public static readonly IReadOnlyList<(string Name, int Weight)> IdlePool = new[]
    {
        (IdleConsole, 3), (IdleSnack, 3), (IdleSleep, 2), (IdleStretch, 2), (IdleWheel, 1),
    };

    public static Dictionary<string, Clip> BuildAll(int bowHue = 0) => BuildAll(bowHue, withBow: true);

    /// <summary>
    /// withBow = false rend les memes frames sans le noeud. Ne sert qu'au controle de
    /// SpriteGen : tout pixel 13 a 15 qui reste alors n'appartient pas au noeud.
    /// </summary>
    public static Dictionary<string, Clip> BuildAll(int bowHue, bool withBow)
    {
        var k = new Kit(bowHue, withBow);
        var all = new[]
        {
            BuildIdle(k), BuildWalk(k), BuildBlink(k), BuildReact(k),
            BuildWorkLaptop(k), BuildWorkBook(k), BuildWorkTerminal(k), BuildWorkLab(k), BuildWorkConductor(k), BuildThink(k),
            BuildPhone(k), BuildCelebrate(k), BuildError(k),
            BuildIdleConsole(k), BuildIdleSnack(k), BuildIdleSleep(k), BuildIdleStretch(k), BuildIdleWheel(k),
            BuildFxPop(), BuildFxSparkle(),
        };
        return all.ToDictionary(c => c.Name);
    }

    /// <summary>Ce que tous les constructeurs de clips partagent : la teinte et le noeud visible ou non.</summary>
    readonly record struct Kit(int Hue, bool Bow)
    {
        public Pose Base()
        {
            var p = Pose.Default;
            p.BowHue = Hue;
            p.HideBow = !Bow;
            return p;
        }
    }

    /// <summary>Decalage des scenes a objet : le personnage glisse a gauche, l'objet prend la droite.</summary>
    const int PropShift = -12;

    static Clip BuildIdle(Kit k)
    {
        int[] bob = { 0, 0, -1, -1, -1, 0 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < bob.Length; i++)
        {
            var p = k.Base();
            p.BodyDy = bob[i];
            p.Squash = bob[i] == 0 ? 0 : -1;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Idle, frames, 5, true, false);
    }

    static Clip BuildWalk(Kit k)
    {
        int[] bob  = { 0, -1, -1, 0, -1, -1 };
        int[] legs = { 1, 1, 0, 2, 2, 0 };
        int[] lean = { 1, 1, 0, -1, -1, 0 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < bob.Length; i++)
        {
            var p = k.Base();
            p.BodyDy = bob[i];
            p.LegPhase = legs[i];
            p.HeadDx = lean[i];
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Walk, frames, 9, true, false);
    }

    static Clip BuildBlink(Kit k)
    {
        var frames = new byte[3][];
        for (int i = 0; i < 3; i++)
        {
            var p = k.Base();
            p.Blink = i == 1;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(Blink, frames, 12, false, false);
    }

    static Clip BuildReact(Kit k)
    {
        //                 anticipation      saut                retombee
        int[] jump  = { 0,  2,  2, -6, -11, -13, -11, -6,  0,  2,  0 };
        int[] squash = { 0, 2,  2, -1,  -1,   0,   0, -1,  2,  1,  0 };
        var frames = new byte[jump.Length][];
        for (int i = 0; i < jump.Length; i++)
        {
            var p = k.Base();
            p.BodyDy = jump[i];
            p.Squash = squash[i];
            p.Heart = i >= 3;
            p.HeartDy = i >= 3 ? -(i - 3) * 3 : 0;
            frames[i] = HamsterSprite.Render(p);
        }
        return new Clip(React, frames, 12, false, true);
    }
}
