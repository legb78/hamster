using System.Drawing;

namespace Hamster.App.Render;

/// <summary>
/// Placement des etiquettes, en pixels ecran relatifs au tampon sprite agrandi. Pur calcul,
/// sans fenetre. Les etiquettes se peignent par-dessus tout le reste : une etiquette de mini
/// qui tourne avec lui passerait sur le visage du principal (arc avant) ou sur son etiquette
/// (arc arriere), d'ou les regles de Place.
/// </summary>
internal static class LabelLayout
{
    /// <summary>Ecart entre une tete et son etiquette, entre deux etiquettes, et autour du visage.</summary>
    public const int Gap = 4;

    /// <summary>Etiquette centree au-dessus d'une tete.</summary>
    public static Rectangle Above(int centerX, int headTop, Size size) =>
        new(centerX - size.Width / 2, headTop - size.Height - Gap, size.Width, size.Height);

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
    /// Etiquette d'un mini. Elle ne couvre jamais le visage du principal ni une etiquette deja
    /// posee, reste dans [minX, maxX] et ne descend pas sous maxY (le bas de la fenetre). Places
    /// essayees dans l'ordre, chacune montee au besoin au-dessus des etiquettes qu'elle
    /// chevauche : au-dessus de sa tete ; a cote du visage, du cote du mini ; sous le visage,
    /// sur le corps du mini ; a cote du visage, de l'autre cote. Cette derniere passe en dernier :
    /// de l'autre cote du principal, l'etiquette semble appartenir a un autre mini.
    /// </summary>
    public static Rectangle Place(Rectangle wanted, Rectangle? face, IReadOnlyList<Rectangle> placed,
        int minX, int maxX, int maxY)
    {
        var first = KeepInside(wanted, minX, maxX);
        if (face is not { } raw) return Stack(first, placed);
        var f = Grow(raw);

        // le cote du mini : celui ou tombe le centre de l'etiquette voulue
        bool rightSide = wanted.X + wanted.Width / 2 >= f.X + f.Width / 2;
        var right = new Rectangle(f.Right, first.Y, first.Width, first.Height);
        var left = new Rectangle(f.Left - first.Width, first.Y, first.Width, first.Height);
        var below = new Rectangle(first.X, f.Bottom, first.Width, first.Height);
        Rectangle[] candidates = { first, rightSide ? right : left, below, rightSide ? left : right };

        Rectangle? faceOnly = null;
        foreach (var c in candidates)
        {
            if (c.Left < minX || c.Right > maxX || c.Bottom > maxY || c.IntersectsWith(f)) continue;
            faceOnly ??= c;
            var r = Stack(c, placed);
            if (!r.IntersectsWith(f) && Hit(r, placed) < 0) return r;
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
    public static Rectangle? FaceRect(RenderClip clip, bool mirror, int left, int top, int scale)
    {
        if (clip.FaceBounds is not { } f) return null;
        int x0 = mirror ? SpriteLibrary.Size - 1 - f.X1 : f.X0;
        int x1 = mirror ? SpriteLibrary.Size - 1 - f.X0 : f.X1;
        return new Rectangle((left + x0) * scale, (top + f.Y0) * scale, (x1 - x0 + 1) * scale, (f.Y1 - f.Y0 + 1) * scale);
    }

    static Rectangle Grow(Rectangle r) => Rectangle.Inflate(r, Gap / 2, Gap / 2);
}
