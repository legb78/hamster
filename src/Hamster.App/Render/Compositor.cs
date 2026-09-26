using Hamster.Art;

namespace Hamster.App.Render;

/// <summary>
/// Composition a la resolution du sprite : un tampon ARGB premultiplie ou l'on pose le
/// principal, les minis et les FX, avant de l'agrandir d'un facteur entier dans la surface
/// de la fenetre. Composer avant d'agrandir coute scale^2 fois moins, et garde la grille
/// du pixel art commune a tous les elements.
/// </summary>
internal sealed class Compositor
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public uint[] Pixels { get; private set; } = Array.Empty<uint>();
    /// <summary>1 la ou le principal montre son visage : les minis de devant n'y peignent jamais.</summary>
    public byte[] Face { get; private set; } = Array.Empty<byte>();

    public void Reset(int width, int height)
    {
        if (width != Width || height != Height)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
            Face = new byte[width * height];
            return;
        }
        Array.Clear(Pixels);
        Array.Clear(Face);
    }

    /// <summary>
    /// Pose une frame indexee (size x size) en (x0, y0). mirror retourne la frame a la lecture.
    /// markFace : note le visage (yeux, museau, nez dans leur rectangle). avoidFace : ne peint
    /// pas la ou un visage a ete note.
    /// </summary>
    public void Blit(byte[] src, int size, int x0, int y0, uint[] palette, bool mirror,
        FaceBox? markFace = null, bool avoidFace = false)
    {
        int w = Width, h = Height;
        var dst = Pixels;
        var face = Face;
        for (int y = 0; y < size; y++)
        {
            int dy = y0 + y;
            if ((uint)dy >= (uint)h) continue;
            int srcRow = y * size;
            int dstRow = dy * w;
            for (int x = 0; x < size; x++)
            {
                int dx = x0 + x;
                if ((uint)dx >= (uint)w) continue;
                byte idx = src[srcRow + (mirror ? size - 1 - x : x)];
                if (idx == 0) continue;
                int di = dstRow + dx;
                if (avoidFace && face[di] != 0) continue;
                uint c = palette[idx];
                dst[di] = (c >> 24) == 255 ? c : Over(c, dst[di]);
                if (markFace is { } fb)
                {
                    int fx = mirror ? size - 1 - x : x;
                    if (fb.Contains(fx, y, idx)) face[di] = 1;
                }
            }
        }
    }

    /// <summary>Rectangle plein d'une couleur ARGB premultipliee, borne au tampon.</summary>
    public void Fill(int x, int y, int w, int h, uint argbPremultiplied)
    {
        for (int j = Math.Max(0, y); j < Math.Min(Height, y + h); j++)
        for (int i = Math.Max(0, x); i < Math.Min(Width, x + w); i++)
            Pixels[j * Width + i] = argbPremultiplied;
    }

    /// <summary>Agrandit le tampon d'un facteur entier dans la surface, au plus proche voisin.</summary>
    public void UpscaleInto(Span<uint> surface, int surfaceWidth, int ox, int oy, int scale)
    {
        int rowWidth = Width * scale;
        for (int y = 0; y < Height; y++)
        {
            var dstRow = surface.Slice((oy + y * scale) * surfaceWidth + ox, rowWidth);
            int src = y * Width;
            bool empty = true;
            for (int x = 0; x < Width; x++)
            {
                uint c = Pixels[src + x];
                if (c == 0) continue;
                empty = false;
                dstRow.Slice(x * scale, scale).Fill(c);
            }
            // la surface sort d'un Clear : une ligne vide n'a rien a recopier
            if (empty) continue;
            for (int k = 1; k < scale; k++)
                dstRow.CopyTo(surface.Slice((oy + y * scale + k) * surfaceWidth + ox, rowWidth));
        }
    }

    /// <summary>Composition "over" en premultiplie : sert a l'ombre, seule couleur translucide.</summary>
    static uint Over(uint src, uint dst)
    {
        uint inv = 255 - (src >> 24);
        uint a = (src >> 24) + ((dst >> 24) * inv + 127) / 255;
        uint r = ((src >> 16) & 0xFF) + (((dst >> 16) & 0xFF) * inv + 127) / 255;
        uint g = ((src >> 8) & 0xFF) + (((dst >> 8) & 0xFF) * inv + 127) / 255;
        uint b = (src & 0xFF) + ((dst & 0xFF) * inv + 127) / 255;
        return (Math.Min(a, 255) << 24) | (Math.Min(r, 255) << 16) | (Math.Min(g, 255) << 8) | Math.Min(b, 255);
    }

    /// <summary>
    /// Rectangle du visage d'une frame de 128 : des yeux (noir, indice 8) jusqu'au bas du
    /// museau, deduit du nez (11 et 12) : le museau descend a 22 px sous le haut du nez et
    /// deborde de 21 px de chaque cote (HamsterSprite). Les coussinets des pattes ont la
    /// couleur du museau et se levent jusqu'au-dessus de la tete : ils ne doivent pas
    /// elargir le rectangle. Null s'il n'y a ni yeux ni nez (FX).
    /// </summary>
    public static FaceBox? FaceOf(byte[] frame, int size)
    {
        var eyes = Box(frame, size, Palette.EyeBlack, Palette.EyeBlack);
        var nose = Box(frame, size, Palette.NoseDark, Palette.Nose);
        if (eyes is not { } e)
            // yeux fermes (clignement) : le visage se deduit du nez seul
            return nose is { } n0 ? new FaceBox(n0.X0 - 34, n0.Y0 - 30, n0.X1 + 34, n0.Y0 + 22, n0.Y0 - 4) : null;
        if (nose is not { } n) return new FaceBox(e.X0, e.Y0, e.X1, e.Y1 + 30, e.Y1);
        return new FaceBox(Math.Min(e.X0, n.X0 - 21), e.Y0, Math.Max(e.X1, n.X1 + 21), Math.Max(e.Y1, n.Y0 + 22), e.Y1);
    }

    static (int X0, int Y0, int X1, int Y1)? Box(byte[] frame, int size, byte from, byte to)
    {
        int x0 = size, y0 = size, x1 = -1, y1 = -1;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            byte idx = frame[y * size + x];
            if (idx < from || idx > to) continue;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }
        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    /// <summary>Premiere ligne non transparente d'une frame.</summary>
    public static int TopRow(byte[] frame, int size)
    {
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            if (frame[y * size + x] != 0) return y;
        return size;
    }
}

/// <summary>
/// Le visage d'une frame : un rectangle, et la ligne du bas des yeux. Dans le rectangle, sont
/// visage le museau et le nez (6, 7, 11, 12), et les yeux avec leurs reflets (8, 9, 10) a
/// hauteur des yeux seulement : plus bas, le blanc est celui de la blouse ou d'une bulle.
/// </summary>
internal readonly record struct FaceBox(int X0, int Y0, int X1, int Y1, int EyesBottom)
{
    public bool Contains(int x, int y, byte idx)
    {
        if (x < X0 || x > X1 || y < Y0 || y > Y1) return false;
        return idx switch
        {
            Palette.MuzzleShade or Palette.Muzzle or Palette.NoseDark or Palette.Nose => true,
            Palette.EyeBlack or Palette.EyeShine or Palette.EyeShine2 => y <= EyesBottom,
            _ => false,
        };
    }
}

/// <summary>
/// Police pixel 3x5 dessinee a la main, pour le badge "+N". Juste le plus et les chiffres :
/// une police systeme a cette taille baverait, et le badge doit suivre la grille du sprite.
/// </summary>
internal static class PixelFont
{
    public const int GlyphWidth = 3, GlyphHeight = 5;

    static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['+'] = new[] { "...", ".#.", "###", ".#.", "..." },
        ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" },
        ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
        ['2'] = new[] { "###", "..#", "###", "#..", "###" },
        ['3'] = new[] { "###", "..#", ".##", "..#", "###" },
        ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" },
        ['5'] = new[] { "###", "#..", "###", "..#", "###" },
        ['6'] = new[] { "###", "#..", "###", "#.#", "###" },
        ['7'] = new[] { "###", "..#", ".#.", ".#.", ".#." },
        ['8'] = new[] { "###", "#.#", "###", "#.#", "###" },
        ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
    };

    public static bool Has(char c) => Glyphs.ContainsKey(c);

    /// <summary>Largeur du texte en pixels : 3 par glyphe, 1 d'espace entre deux.</summary>
    public static int Measure(string text) => text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) - 1;

    /// <summary>Allume les pixels du texte ; rend le nombre de pixels allumes.</summary>
    public static int Draw(string text, int x, int y, Action<int, int> plot)
    {
        int lit = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(text[i], out var rows)) continue;
            for (int gy = 0; gy < GlyphHeight; gy++)
            for (int gx = 0; gx < GlyphWidth; gx++)
                if (rows[gy][gx] == '#') { plot(x + i * (GlyphWidth + 1) + gx, y + gy); lit++; }
        }
        return lit;
    }
}
