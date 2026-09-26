namespace Hamster.Art;

/// <summary>Une frame : 128x128 indices de palette. Rien d'autre ne touche aux pixels.</summary>
public sealed class PixelCanvas
{
    public const int Size = 128;

    /// <summary>Ligne de base commune a tous les clips : les pieds se posent ici, sinon ca tressaute.</summary>
    public const int Baseline = 121;

    public readonly byte[] Px = new byte[Size * Size];

    public void Clear() => Array.Clear(Px);

    public void Set(int x, int y, byte c)
    {
        if ((uint)x < Size && (uint)y < Size) Px[y * Size + x] = c;
    }

    public byte Get(int x, int y) => (uint)x < Size && (uint)y < Size ? Px[y * Size + x] : (byte)0;

    public void HLine(int x0, int x1, int y, byte c)
    {
        if ((uint)y >= Size) return;
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (x1 < 0 || x0 >= Size) return;
        x0 = Math.Max(0, x0);
        x1 = Math.Min(Size - 1, x1);
        int row = y * Size;
        for (int x = x0; x <= x1; x++) Px[row + x] = c;
    }

    public void VLine(int x, int y0, int y1, byte c)
    {
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Set(x, y, c);
    }

    public void Rect(int x, int y, int w, int h, byte c)
    {
        for (int j = 0; j < h; j++) HLine(x, x + w - 1, y + j, c);
    }

    /// <summary>Ellipse pleine, rayons inclusifs.</summary>
    public void Ellipse(int cx, int cy, int rx, int ry, byte c)
    {
        if (rx <= 0 || ry <= 0) return;
        for (int dy = -ry; dy <= ry; dy++)
        {
            double t = 1.0 - (double)(dy * dy) / (double)(ry * ry);
            if (t < 0) continue;
            int dx = (int)(rx * Math.Sqrt(t) + 0.5);
            HLine(cx - dx, cx + dx, cy + dy, c);
        }
    }

    /// <summary>Ellipse pleine limitee a une bande verticale : sert aux ombrages internes.</summary>
    public void EllipseClipped(int cx, int cy, int rx, int ry, byte c, int yMin, int yMax)
    {
        if (rx <= 0 || ry <= 0) return;
        for (int dy = -ry; dy <= ry; dy++)
        {
            int y = cy + dy;
            if (y < yMin || y > yMax) continue;
            double t = 1.0 - (double)(dy * dy) / (double)(ry * ry);
            if (t < 0) continue;
            int dx = (int)(rx * Math.Sqrt(t) + 0.5);
            HLine(cx - dx, cx + dx, y, c);
        }
    }

    /// <summary>
    /// Ellipse pleine qui ne peint que par-dessus une couleur donnee. Sert a poser
    /// un ombrage a bord courbe sans deborder de la silhouette : decouper une
    /// ellipse a une hauteur fixe donne un bord droit qui lit comme un casque.
    /// </summary>
    public void EllipseMasked(int cx, int cy, int rx, int ry, byte c, byte onlyOver)
    {
        if (rx <= 0 || ry <= 0) return;
        for (int dy = -ry; dy <= ry; dy++)
        {
            int y = cy + dy;
            if ((uint)y >= Size) continue;
            double t = 1.0 - (double)(dy * dy) / (double)(ry * ry);
            if (t < 0) continue;
            int dx = (int)(rx * Math.Sqrt(t) + 0.5);
            int x0 = Math.Max(0, cx - dx), x1 = Math.Min(Size - 1, cx + dx);
            int row = y * Size;
            for (int x = x0; x <= x1; x++) if (Px[row + x] == onlyOver) Px[row + x] = c;
        }
    }

    /// <summary>Ellipse qui ne peint que le transparent : une ombre passe ainsi SOUS ce qui est deja pose.</summary>
    public void EllipseUnder(int cx, int cy, int rx, int ry, byte c) => EllipseMasked(cx, cy, rx, ry, c, Palette.Transparent);

    /// <summary>Anneau : l'ellipse (rx, ry) privee de l'ellipse (rx - t, ry - t). Memes spans que Ellipse.</summary>
    public void Ring(int cx, int cy, int rx, int ry, int t, byte c)
    {
        if (rx <= 0 || ry <= 0) return;
        int irx = rx - t, iry = ry - t;
        for (int dy = -ry; dy <= ry; dy++)
        {
            int xo = Span(rx, ry, dy);
            if (xo < 0) continue;
            int xi = irx > 0 && iry > 0 && Math.Abs(dy) <= iry ? Span(irx, iry, dy) : -1;
            if (xi < 0) { HLine(cx - xo, cx + xo, cy + dy, c); continue; }
            // gardes explicites : HLine permute ses bornes, un span vide deviendrait un trait
            if (xo > xi) { HLine(cx - xo, cx - xi - 1, cy + dy, c); HLine(cx + xi + 1, cx + xo, cy + dy, c); }
        }
    }

    static int Span(int rx, int ry, int dy)
    {
        double t = 1.0 - (double)(dy * dy) / (double)(ry * ry);
        return t < 0 ? -1 : (int)(rx * Math.Sqrt(t) + 0.5);
    }

    /// <summary>Trait de Bresenham, 1 px.</summary>
    public void Line(int x0, int y0, int x1, int y1, byte c)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Polygone CONVEXE plein, par balayage de lignes. Suffit aux objets : couvercles, pages, socles.</summary>
    public void Polygon(byte c, params (int X, int Y)[] pts)
    {
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in pts) { minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
        for (int y = minY; y <= maxY; y++)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                if (a.Y == b.Y)
                {
                    if (a.Y != y) continue;
                    lo = Math.Min(lo, Math.Min(a.X, b.X));
                    hi = Math.Max(hi, Math.Max(a.X, b.X));
                    continue;
                }
                if (y < Math.Min(a.Y, b.Y) || y > Math.Max(a.Y, b.Y)) continue;
                double x = a.X + (double)(y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                lo = Math.Min(lo, x);
                hi = Math.Max(hi, x);
            }
            if (lo <= hi) HLine((int)Math.Round(lo), (int)Math.Round(hi), y, c);
        }
    }

    /// <summary>
    /// Motif ASCII : tout caractere autre que ' ' et '.' prend l'encre. Sert aux petits
    /// motifs (lettres, boutons, stickers) qu'aucune ellipse ne rend proprement a 1x.
    /// </summary>
    public void Stamp(int x, int y, string[] rows, byte ink)
    {
        for (int j = 0; j < rows.Length; j++)
        for (int i = 0; i < rows[j].Length; i++)
            if (rows[j][i] != ' ' && rows[j][i] != '.') Set(x + i, y + j, ink);
    }

    /// <summary>Pose une couche par-dessus : seuls ses pixels non transparents comptent.</summary>
    public void Over(PixelCanvas top)
    {
        var src = top.Px;
        for (int i = 0; i < Px.Length; i++) if (src[i] != 0) Px[i] = src[i];
    }

    /// <summary>Compte les pixels d'un indice donne : sert aux controles de SpriteGen.</summary>
    public static int Count(byte[] indices, byte c)
    {
        int n = 0;
        foreach (byte b in indices) if (b == c) n++;
        return n;
    }

    /// <summary>
    /// Remappe une frame par table de 32 entrees. C'est ainsi que les minis recolorent
    /// leur noeud : une table, pas trois Swap enchaines qui pourraient se marcher dessus.
    /// </summary>
    public static byte[] Remap(byte[] indices, byte[] table)
    {
        var o = new byte[indices.Length];
        for (int i = 0; i < indices.Length; i++) o[i] = table[indices[i]];
        return o;
    }

    /// <summary>Contour 1 px pose a l'exterieur de la silhouette deja dessinee.</summary>
    public void OutlineSilhouette(byte c)
    {
        var src = (byte[])Px.Clone();
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (src[y * Size + x] != 0) continue;
            bool touches =
                (x > 0        && src[y * Size + x - 1] != 0) ||
                (x < Size - 1 && src[y * Size + x + 1] != 0) ||
                (y > 0        && src[(y - 1) * Size + x] != 0) ||
                (y < Size - 1 && src[(y + 1) * Size + x] != 0);
            if (touches) Px[y * Size + x] = c;
        }
    }

    /// <summary>Recolorisation : sert aux noeuds des minis.</summary>
    public void Swap(byte from, byte to)
    {
        for (int i = 0; i < Px.Length; i++) if (Px[i] == from) Px[i] = to;
    }

    public byte[] Snapshot() => (byte[])Px.Clone();

    public static byte[] Mirror(byte[] src)
    {
        var o = new byte[src.Length];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            o[y * Size + x] = src[y * Size + (Size - 1 - x)];
        return o;
    }

    public static uint[] ToArgb(byte[] indices, uint[] premultipliedPalette)
    {
        var o = new uint[indices.Length];
        for (int i = 0; i < indices.Length; i++) o[i] = premultipliedPalette[indices[i]];
        return o;
    }

    /// <summary>
    /// Variante mini : box-filter 2x2 puis rappel sur la palette. Facteur entier,
    /// donc 1 px du mini vaut exactement 4 px du grand. Le transparent ne l'emporte
    /// qu'a la majorite absolue, sinon les silhouettes maigrissent a chaque passe.
    /// </summary>
    public static byte[] Downscale2x(byte[] src)
    {
        const int n = Size / 2;
        var o = new byte[n * n];
        var tally = new int[Palette.Count];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            Array.Clear(tally);
            tally[src[(y * 2) * Size + x * 2]]++;
            tally[src[(y * 2) * Size + x * 2 + 1]]++;
            tally[src[(y * 2 + 1) * Size + x * 2]]++;
            tally[src[(y * 2 + 1) * Size + x * 2 + 1]]++;

            int best = 0, bestN = 0;
            for (int i = 1; i < Palette.Count; i++) if (tally[i] > bestN) { bestN = tally[i]; best = i; }
            o[y * n + x] = (byte)(tally[0] >= 3 || bestN == 0 ? 0 : best);
        }
        return o;
    }
}
