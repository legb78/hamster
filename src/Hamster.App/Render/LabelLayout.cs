using System.Drawing;

namespace Hamster.App.Render;

/// <summary>
/// Placement des etiquettes, en pixels ecran relatifs au tampon sprite agrandi. Pur calcul,
/// sans fenetre. Les etiquettes se peignent par-dessus tout le reste : une etiquette de mini
/// qui tourne avec lui passerait sur le visage du principal (arc avant), sur sa bulle, son
/// telephone ou son etiquette (arc arriere), d'ou les regles de Place.
/// </summary>
internal static class LabelLayout
{
    /// <summary>Ecart entre une tete et son etiquette, entre deux etiquettes, et autour du visage.</summary>
    public const int Gap = 4;

    /// <summary>Etiquette centree au-dessus d'une tete.</summary>
    public static Rectangle Above(int centerX, int headTop, Size size) =>
        new(centerX - size.Width / 2, headTop - size.Height - Gap, size.Width, size.Height);

    /// <summary>
    /// Bornes [minX, maxX] des etiquettes, en pixels relatifs au tampon sprite agrandi : les
    /// bords de la zone de travail de l'ecran. Le tampon, large de spriteW, est centre sur le
    /// principal (PetController.Render) : son bord gauche est a centerScreenX - spriteW / 2.
    /// </summary>
    public static (int MinX, int MaxX) ScreenBounds(Rectangle workingArea, int centerScreenX, int spriteW)
    {
        int spriteLeft = centerScreenX - spriteW / 2;
        return (workingArea.Left - spriteLeft, workingArea.Right - spriteLeft);
    }

    /// <summary>
    /// Decale r horizontalement pour que ses bords restent dans [minX, maxX] : le bord de
    /// l'ecran, pour qu'aucune etiquette ne soit coupee. Plus large que la zone : calee a gauche.
    /// </summary>
    public static Rectangle KeepInside(Rectangle r, int minX, int maxX)
    {
        int x = r.X;
        if (x + r.Width > maxX) x = maxX - r.Width;
        if (x < minX) x = minX;
        return new Rectangle(x, r.Y, r.Width, r.Height);
    }

    /// <summary>
    /// Etiquette d'un mini dont les pieds sont en (x, feet), pixels sprite, rebond non compris ;
    /// bob est le rebond du moment, en pixels sprite (Orbit.BobAt). La place se choisit sans le
    /// rebond, qui ne decale ensuite que Y : sinon, un pixel de rebond suffisait a changer de
    /// place, et l'etiquette sautait de plus de 100 px au rythme du sautillement. La place est
    /// choisie pour toute l'amplitude du rebond (Orbit.Bob) : l'etiquette affichee n'empiete
    /// jamais sur le visage, a aucun moment. Reserved couvre cette amplitude : les etiquettes
    /// posees ensuite l'evitent, quel que soit le rebond de chacune.
    /// </summary>
    public static (Rectangle Shown, Rectangle Reserved) MiniLabel(int x, int feet, int bob, int topRow, Size size, int scale,
        Rectangle? face, Rectangle? head, IReadOnlyList<Rectangle> placed, int minX, int maxX, int maxY)
    {
        int top = feet - SpriteLibrary.MiniBaseline + topRow / 2;
        int sweep = Orbit.Bob * scale;
        var r = Place(Above(x * scale, top * scale, size), face, head, placed, minX, maxX, maxY, sweep);
        return (new Rectangle(r.X, r.Y + bob * scale, r.Width, r.Height), Swept(r, sweep));
    }

    /// <summary>La bande que r balaie en montant et descendant de sweep pixels.</summary>
    public static Rectangle Swept(Rectangle r, int sweep) => new(r.X, r.Y - sweep, r.Width, r.Height + 2 * sweep);

    /// <summary>
    /// Etiquette d'un mini. Elle ne couvre jamais le visage du principal, evite sa tete (bulle,
    /// combine, oreilles : head) quand la place le permet, ne couvre pas une etiquette deja
    /// posee, reste dans [minX, maxX] et ne descend pas sous maxY (le bas de la fenetre). Places
    /// essayees dans l'ordre, chacune montee au besoin au-dessus des etiquettes qu'elle
    /// chevauche : au-dessus de sa tete ; a cote de l'obstacle, du cote du mini ; sous
    /// l'obstacle, sur le corps du mini ; a cote de l'obstacle, de l'autre cote. Cette derniere
    /// passe en dernier : de l'autre cote du principal, l'etiquette semble appartenir a un autre
    /// mini. L'obstacle est la tete, puis, si rien ne tient, le visage seul. Chaque place vaut
    /// pour toute la bande de rebond (sweep au-dessus et au-dessous) ; le rectangle rendu est
    /// celui du milieu de la bande.
    /// </summary>
    public static Rectangle Place(Rectangle wanted, Rectangle? face, Rectangle? head, IReadOnlyList<Rectangle> placed,
        int minX, int maxX, int maxY, int sweep = 0)
    {
        var r = PlaceBand(Swept(wanted, sweep), face, head, placed, minX, maxX, maxY);
        return new Rectangle(r.X, r.Y + sweep, wanted.Width, wanted.Height);
    }

    static Rectangle PlaceBand(Rectangle wanted, Rectangle? face, Rectangle? head, IReadOnlyList<Rectangle> placed,
        int minX, int maxX, int maxY)
    {
        var first = KeepInside(wanted, minX, maxX);
        if (face is not { } rawFace) return Stack(first, placed);
        var obstacles = head is { } rawHead
            ? new[] { Grow(Rectangle.Union(rawHead, rawFace)), Grow(rawFace) }
            : new[] { Grow(rawFace) };

        Rectangle? faceOnly = null;
        foreach (var f in obstacles)
        {
            // le cote du mini : celui ou tombe le centre de l'etiquette voulue
            bool rightSide = wanted.X + wanted.Width / 2 >= f.X + f.Width / 2;
            var right = new Rectangle(f.Right, first.Y, first.Width, first.Height);
            var left = new Rectangle(f.Left - first.Width, first.Y, first.Width, first.Height);
            var below = new Rectangle(first.X, f.Bottom, first.Width, first.Height);
            Rectangle[] candidates = { first, rightSide ? right : left, below, rightSide ? left : right };
            foreach (var c in candidates)
            {
                // chaque obstacle contient le visage : une place qui l'evite evite le visage
                if (c.Left < minX || c.Right > maxX || c.Bottom > maxY || c.IntersectsWith(f)) continue;
                faceOnly ??= c;
                var r = Stack(c, placed);
                if (!r.IntersectsWith(f) && Hit(r, placed) < 0) return r;
            }
        }
        // rien ne tient : le visage d'abord, quitte a chevaucher une etiquette
        return faceOnly ?? first;
    }

    /// <summary>Monte r au-dessus de chaque etiquette posee qu'elle chevauche ; X ne bouge pas.</summary>
    static Rectangle Stack(Rectangle r, IReadOnlyList<Rectangle> placed)
    {
        // chaque montee passe au-dessus d'une etiquette : au plus une par etiquette posee
        for (int pass = 0; pass <= placed.Count; pass++)
        {
            int hit = Hit(r, placed);
            if (hit < 0) break;
            r.Y = placed[hit].Top - Gap - r.Height;
        }
        return r;
    }

    static int Hit(Rectangle r, IReadOnlyList<Rectangle> placed)
    {
        for (int i = 0; i < placed.Count; i++)
            if (r.IntersectsWith(Grow(placed[i]))) return i;
        return -1;
    }

    /// <summary>
    /// Visage du principal, toutes frames du clip, en pixels ecran relatifs au tampon agrandi :
    /// principal pose en (left, top) du tampon sprite, retourne si mirror, agrandi scale fois.
    /// </summary>
    public static Rectangle? FaceRect(RenderClip clip, bool mirror, int left, int top, int scale) =>
        clip.FaceBounds is { } f ? ToScreen(f, mirror, left, top, scale) : null;

    /// <summary>Tete du principal (RenderClip.HeadBounds), dans le meme repere que FaceRect.</summary>
    public static Rectangle? HeadRect(RenderClip clip, bool mirror, int left, int top, int scale) =>
        clip.HeadBounds is { } h ? ToScreen(h, mirror, left, top, scale) : null;

    static Rectangle ToScreen((int X0, int Y0, int X1, int Y1) b, bool mirror, int left, int top, int scale)
    {
        int x0 = mirror ? SpriteLibrary.Size - 1 - b.X1 : b.X0;
        int x1 = mirror ? SpriteLibrary.Size - 1 - b.X0 : b.X1;
        return new Rectangle((left + x0) * scale, (top + b.Y0) * scale, (x1 - x0 + 1) * scale, (b.Y1 - b.Y0 + 1) * scale);
    }

    static Rectangle Grow(Rectangle r) => Rectangle.Inflate(r, Gap / 2, Gap / 2);
}
