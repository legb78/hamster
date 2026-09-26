namespace Hamster.Art;

/// <summary>
/// Clips de travail (etat Working). Meme principe partout : le personnage glisse de
/// 12 px a gauche, l'objet est pose au sol a sa droite, et les yeux glissent vers lui.
/// Les mains passent sur leur propre couche, devant l'objet.
/// </summary>
public static partial class Clips
{
    /// <summary>Tape sur un laptop a stickers, les deux mains en alternance.</summary>
    static Clip BuildWorkLaptop(Kit k)
    {
        int[] bob = { 0, -1, 0, 0, -1, 0 };
        int[] a   = { 3, 0, 2, 0, 3, 0 };   // hauteur de la main gauche au-dessus du clavier
        int[] b   = { 0, 3, 0, 3, 0, 2 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.BodyDy = bob[i];
            p.EyeDx = 3;
            p.EyeDy = 2;
            int f = i;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(Props.Laptop)
                .Layer(c =>
                {
                    HamsterSprite.DrawPaw(c, 75, 112 - a[f], far: true);
                    HamsterSprite.DrawPaw(c, 85, 112 - b[f]);
                })
                .Layer(c => KeyTicks(c, a[f] == 0 && a[(f + 5) % 6] > 0 ? 75 : -1, b[f] == 0 && b[(f + 5) % 6] > 0 ? 85 : -1), outline: false)
                .Shadow(94, 26)
                .Build();
        }
        return new Clip(WorkLaptop, frames, 10, true, false);
    }

    /// <summary>Deux petits traits au-dessus de la main qui vient de frapper une touche.</summary>
    static void KeyTicks(PixelCanvas c, int x1, int x2)
    {
        foreach (int x in new[] { x1, x2 })
        {
            if (x < 0) continue;
            c.Set(x - 5, 104, Palette.Outline);
            c.Set(x - 4, 105, Palette.Outline);
            c.Set(x + 5, 104, Palette.Outline);
            c.Set(x + 4, 105, Palette.Outline);
            c.Set(x, 102, Palette.Outline);
            c.Set(x, 103, Palette.Outline);
        }
    }

    /// <summary>
    /// Lit un gros livre ouvert a la loupe, devant une pile de livres. La loupe balaie
    /// la page, puis la page tourne.
    /// </summary>
    static Clip BuildWorkBook(Kit k)
    {
        // position de la loupe ; -1 = page qui tourne, la loupe se repose
        int[] lens = { 90, 94, 98, 102, -1, -1, -1, 90 };
        int[] page = { -1, -1, -1, -1, 0, 1, 2, -1 };
        var frames = new byte[lens.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.EyeDx = 3;
            p.EyeDy = 3;
            p.BodyDy = i == 3 || i == 4 ? -1 : 0;
            int f = i;
            frames[i] = new Scene()
                .Layer(Props.BookStack)
                .Hamster(p)
                .Layer(Props.OpenBook)
                .Layer(c => { if (page[f] >= 0) Props.TurningPage(c, page[f]); })
                .Layer(c =>
                {
                    var (lx, ly) = LensAt(lens[f]);
                    Props.Magnifier(c, lx, ly);
                })
                .Layer(c =>
                {
                    var (lx, ly) = LensAt(lens[f]);
                    HamsterSprite.DrawPaw(c, lx - 13, ly + 12);
                })
                .Shadow(88, 22)
                .Shadow(111, 14)
                .Build();
        }
        return new Clip(WorkBook, frames, 8, true, false);
    }

    /// <summary>La loupe au repos pendant que la page tourne, sinon au-dessus de la page.</summary>
    static (int X, int Y) LensAt(int x) => x < 0 ? (84, 97) : (x, 101);

    /// <summary>Terminal : ecran noir, texte vert qui defile, lunettes rondes qui refletent l'ecran.</summary>
    static Clip BuildWorkTerminal(Kit k)
    {
        int[] a = { 3, 0, 0, 3, 0, 0, 2, 0 };
        int[] b = { 0, 0, 3, 0, 0, 3, 0, 0 };
        int[] lean = { 0, 0, 0, 1, 1, 1, 0, 0 };
        var frames = new byte[a.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.HeadDx = lean[i];
            p.EyeDx = 3;
            p.EyeDy = 1;
            p.Glasses = true;
            p.GlassesGlint = Palette.ScreenGreen;
            int f = i;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(c => Props.Terminal(c, f))
                .Layer(Props.Keyboard)
                .Layer(c =>
                {
                    HamsterSprite.DrawPaw(c, 73, 113 - a[f], far: true);
                    HamsterSprite.DrawPaw(c, 84, 113 - b[f]);
                })
                .Shadow(109, 16)
                .Shadow(81, 18)
                .Build();
        }
        return new Clip(WorkTerminal, frames, 8, true, false);
    }

    /// <summary>
    /// Labo : blouse blanche, eprouvettes qui bullent au sol, tableau noir a formules
    /// derriere. La main se pose sur le support, les yeux suivent les bulles.
    /// </summary>
    static Clip BuildWorkLab(Kit k)
    {
        int[] bob  = { 0, 0, -1, -1, 0, 0 };
        int[] look = { 3, 3, 2, 2, 3, 3 };
        var frames = new byte[bob.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.BodyDy = bob[i];
            p.Coat = true;
            p.EyeDx = 3;
            p.EyeDy = look[i];
            int f = i;
            frames[i] = new Scene()
                .Layer(Props.Blackboard)
                .Hamster(p)
                .Layer(c => Props.TestTubes(c, f))
                .Layer(c => HamsterSprite.DrawPaw(c, 91, 107 + bob[f]))
                .Shadow(106, 18)
                .Build();
        }
        return new Clip(WorkLab, frames, 6, true, false) { Mirrorable = false };
    }

    /// <summary>
    /// Chef d'orchestre : la baguette designe tour a tour les cases d'un organigramme
    /// sur un tableau blanc, la case designee s'allume : l'agent qui distribue le travail.
    /// </summary>
    static Clip BuildWorkConductor(Kit k)
    {
        // cible de la baguette : 0, 1, 2 = une case ; -1 = entre deux, la baguette file
        int[] target = { 0, -1, 1, -1, 2, -1 };
        int[] bob    = { 0, -1, 0, -1, 0, -1 };
        var frames = new byte[target.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.BodyDy = bob[i];
            p.EyeDx = 3;
            p.EyeDy = -1;
            int f = i;
            int t = target[i] >= 0 ? target[i] : (target[(i + 5) % 6] + 1) % 3;
            var box = Props.Boxes[t];
            // entre deux cases, la baguette vise le milieu du trajet
            var prev = Props.Boxes[(t + 2) % 3];
            int tx = target[i] >= 0 ? box.X + 5 : (box.X + prev.X) / 2 + 5;
            int ty = target[i] >= 0 ? box.Y + 3 : (box.Y + prev.Y) / 2 + 3;
            int hx = 93, hy = 98 + bob[i];
            frames[i] = new Scene()
                .Layer(c => Props.Whiteboard(c, target[f]))
                .Hamster(p)
                .Layer(c => Props.Baton(c, hx, hy, tx, ty, target[f] < 0), outline: false)
                .Layer(c => HamsterSprite.DrawPaw(c, hx, hy))
                .Shadow(110, 14)
                .Build();
        }
        return new Clip(WorkConductor, frames, 6, true, false);
    }

    /// <summary>Reflechit : patte au menton, bulle de pensee ou les points arrivent un par un.</summary>
    static Clip BuildThink(Kit k)
    {
        int[] dots = { 1, 1, 2, 2, 3, 3 };
        int[] tilt = { 0, 0, 0, 1, 1, 1 };
        int[] tap  = { 0, 1, 0, 0, 1, 0 };
        var frames = new byte[dots.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.HeadDx = tilt[i];
            p.EyeDx = -3;
            p.EyeDy = -2;
            int f = i;
            frames[i] = new Scene()
                .Layer(Props.ThoughtTrail)
                .Layer(Props.ThoughtBubble)
                .Layer(c => Props.ThoughtDots(c, dots[f]), outline: false)
                .Hamster(p)
                .Layer(c => HamsterSprite.DrawPaw(c, 79 + tilt[f], 106 - tap[f]))
                .Build();
        }
        return new Clip(Think, frames, 6, true, false);
    }
}
