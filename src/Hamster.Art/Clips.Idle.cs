namespace Hamster.Art;

/// <summary>Variantes de repos (etat Idle) : console, grignotage, sieste.</summary>
public static partial class Clips
{
    /// <summary>Joue a une console portable posee debout au sol, une main sur la croix.</summary>
    static Clip BuildIdleConsole(Kit k)
    {
        int[] tap = { 0, 1, 0, 1, 1, 0 };
        int[] bob = { 0, 0, -1, 0, 0, -1 };
        var frames = new byte[tap.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.BodyDy = bob[i];
            p.EyeDx = 3;
            p.EyeDy = 3;
            int f = i;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(c => Props.GameConsole(c, f))
                .Layer(c => HamsterSprite.DrawPaw(c, 96, 113 + tap[f]))
                .Layer(c => c.Stamp(113 + f / 2, 86 - f * 2, Props.Note, Palette.Outline), outline: false)
                .Shadow(101, 11)
                .Build();
        }
        return new Clip(IdleConsole, frames, 6, true, false);
    }

    /// <summary>
    /// Grignote des graines : la main les prend dans le tas et les fourre dans la bajoue
    /// par le cote, jamais devant le visage. Les bajoues gonflent, puis il avale.
    /// </summary>
    static Clip BuildIdleSnack(Kit k)
    {
        int[] cheeks = { 1, 1, 2, 3, 3, 3, 4, 5, 4, 2 };
        // main : 0 = au tas, 1 = en chemin avec une graine, 2 = contre la bajoue, 3 = au repos
        int[] hand   = { 0, 1, 2, 3, 0, 1, 2, 3, 3, 3 };
        int[] chew   = { 0, 0, 0, 1, 0, 0, 0, 1, 2, 0 };
        var frames = new byte[cheeks.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = SnackShift;
            p.Cheeks = cheeks[i];
            p.Mouth = chew[i] > 0 ? Mouth.Chew : Mouth.Droop;
            p.ChewPhase = chew[i];
            bool looking = hand[i] == 0;
            p.EyeDx = looking ? 3 : 0;
            p.EyeDy = looking ? 3 : 0;
            int f = i;
            // bord exterieur de la bajoue droite : centre a 30 + 2n du milieu, rayon 6 + 2n
            int cheekEdge = HamsterSprite.CenterX + SnackShift + 36 + cheeks[i] * 4;
            (int X, int Y) at = hand[i] switch
            {
                0 => (108, 106),
                1 => (106, 99),
                2 => (cheekEdge + 3, 94),
                _ => (94, 104),
            };
            frames[i] = new Scene()
                .Layer(c => Props.SeedBowl(c, f < 4 ? 0 : 1))
                .Hamster(p)
                .Layer(c =>
                {
                    if (hand[f] == 1 || hand[f] == 2) Props.Seed(c, at.X - 4, at.Y - 3);
                })
                .Layer(c => HamsterSprite.DrawPaw(c, at.X, at.Y))
                .Shadow(113, 14)
                .Build();
        }
        return new Clip(IdleSnack, frames, 6, true, false);
    }

    /// <summary>
    /// S'etire : le corps s'allonge vers le haut, les mains passent au-dessus du crane
    /// (a gauche du noeud, jamais dessus), baillement, yeux plisses mais ouverts.
    /// </summary>
    static Clip BuildIdleStretch(Kit k)
    {
        int[] stretch = { 0, -2, -5, -5, -2, 0 };
        var frames = new byte[stretch.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            // etirement pur : un BodyDy en plus decollait le corps de ses pieds
            p.Squash = stretch[i];
            bool peak = stretch[i] <= -5;
            p.Mouth = peak ? Mouth.Yawn : stretch[i] < 0 ? Mouth.Talk : Mouth.Droop;
            p.Lid = peak ? 40 : 0;
            int f = i;
            int cy = HamsterSprite.CenterY + p.BodyDy + p.Squash;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(c =>
                {
                    if (stretch[f] == 0) return;
                    int lift = peak ? 0 : 6;
                    HamsterSprite.DrawPaw(c, 64 - 16, cy - 49 + lift);
                    HamsterSprite.DrawPaw(c, 64 + 4, cy - 51 + lift);
                })
                .Build();
        }
        return new Clip(IdleStretch, frames, 6, true, false);
    }

    /// <summary>
    /// Skate, la variante rare : il roule sur place, un peu penche en avant, traits de
    /// vitesse derriere lui. Les roues touchent la ligne de base, lui est porte par la planche.
    /// </summary>
    static Clip BuildIdleWheel(Kit k)
    {
        int[] bob  = { -11, -11, -12, -11 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.BodyDy = bob[i];
            p.Airborne = true;
            p.Shadow = false;
            p.HeadDx = 1;
            p.EyeDx = 2;
            int f = i;
            frames[i] = new Scene()
                .Layer(c => Props.SpeedLines(c, f), outline: false)
                .Hamster(p)
                .Layer(c => Props.Skateboard(c, f))
                .Shadow(65, 36)
                .Build();
        }
        return new Clip(IdleWheel, frames, 8, true, false);
    }

    /// <summary>
    /// Decalage reduit pour le grignotage : les bajoues pleines elargissent la silhouette
    /// des DEUX cotes, un decalage de 12 px couperait la bajoue gauche au bord du canvas.
    /// </summary>
    const int SnackShift = -6;

    /// <summary>
    /// Sieste : paupieres lourdes mais yeux noirs encore visibles (signature tenue),
    /// respiration lente, Z qui montent au-dessus de l'oreille, loin du noeud.
    /// </summary>
    static Clip BuildIdleSleep(Kit k)
    {
        int[] breathe = { 0, 0, 1, 1, 1, 0 };
        var frames = new byte[breathe.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.Squash = breathe[i];
            p.Lid = 55;
            p.EyeDy = 1;
            p.Mouth = breathe[i] == 1 ? Mouth.Talk : Mouth.Droop;
            int f = i;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(c =>
                {
                    // trois Z decales de 2 frames : la boucle de 6 frames reste continue
                    for (int z = 0; z < 3; z++)
                    {
                        int t = (f + z * 2) % 6;
                        Props.Z(c, 34 - t * 3, 30 - t * 5, t / 2);
                    }
                })
                .Build();
        }
        return new Clip(IdleSleep, frames, 5, true, false) { Mirrorable = false };
    }
}
