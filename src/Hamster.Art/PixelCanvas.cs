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
