using Hamster.Art;

namespace Hamster.App.Render;

/// <summary>
/// Un clip pret a l'affichage. Les frames restent en indices de palette (1 octet par pixel) :
/// la conversion en ARGB se fait au blit, miroir compris. En ARGB avec miroir, les 148 frames
/// pesaient 18,5 Mio ; en indices, 2,4 Mio, plus 0,6 Mio pour le cache des minis.
/// </summary>
internal sealed class RenderClip
{
    public required string Name { get; init; }
    public required byte[][] Frames { get; init; }
    public required int Fps { get; init; }
    public required bool Loop { get; init; }
    public required bool LeavesBaseline { get; init; }
    /// <summary>Faux pour les clips a texte : on ne les affiche jamais en miroir.</summary>
    public required bool Mirrorable { get; init; }

    byte[]?[]? _mini;
    FaceBox?[]? _faces;
    bool[]? _facesDone;
    int? _topRow;
    (int X0, int Y0, int X1, int Y1)? _faceBounds, _headBounds;
    bool _faceBoundsDone, _headBoundsDone;

    public int FrameCount => Frames.Length;

    /// <summary>Rectangle du visage de la frame, calcule a la premiere demande.</summary>
    public FaceBox? Face(int index)
    {
        _faces ??= new FaceBox?[Frames.Length];
        _facesDone ??= new bool[Frames.Length];
        if (!_facesDone[index])
        {
            _faces[index] = Compositor.FaceOf(Frames[index], PixelCanvas.Size);
            _facesDone[index] = true;
        }
        return _faces[index];
    }

    /// <summary>Ligne la plus haute atteinte par le clip, toutes frames confondues : l'etiquette se pose au-dessus.</summary>
    public int TopRow => _topRow ??= Frames.Min(f => Compositor.TopRow(f, PixelCanvas.Size));

    /// <summary>
    /// Rectangle qui couvre le visage de toutes les frames du clip, en pixels de frame, bornes
    /// comprises ; null sans visage (FX). Les etiquettes des minis l'evitent : sur toutes les
    /// frames plutot que sur la frame courante, pour qu'elles ne sautent pas a chaque image.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1)? FaceBounds
    {
        get
        {
            if (_faceBoundsDone) return _faceBounds;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (int i = 0; i < Frames.Length; i++)
            {
                if (Face(i) is not { } b) continue;
                x0 = Math.Min(x0, b.X0); y0 = Math.Min(y0, b.Y0);
                x1 = Math.Max(x1, b.X1); y1 = Math.Max(y1, b.Y1);
            }
            _faceBounds = x1 < x0 ? null : (x0, y0, x1, y1);
            _faceBoundsDone = true;
            return _faceBounds;
        }
    }

    /// <summary>
    /// Rectangle des pixels opaques du clip, toutes frames, de la ligne du haut jusqu'au bas du
    /// visage : la tete et ce qui l'entoure, bulle du telephone, combine, noeud, pattes levees.
    /// Null sans visage. Les etiquettes des minis l'evitent quand la place le permet.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1)? HeadBounds
    {
        get
        {
            if (_headBoundsDone) return _headBounds;
            _headBoundsDone = true;
            if (FaceBounds is not { } face) return _headBounds = null;
            int n = PixelCanvas.Size;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var frame in Frames)
                for (int y = 0; y <= face.Y1 && y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (frame[y * n + x] == 0) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            return _headBounds = (Math.Min(x0, face.X0), Math.Min(y0, face.Y0), Math.Max(x1, face.X1), Math.Max(y1, face.Y1));
        }
    }

    /// <summary>La frame reduite de moitie pour les minis, calculee a la premiere demande puis gardee.</summary>
    public byte[] Mini(int index)
    {
        _mini ??= new byte[]?[Frames.Length];
        return _mini[index] ??= PixelCanvas.Downscale2x(Frames[index]);
    }
}

/// <summary>
/// Charge les clips proceduraux de Hamster.Art. Une seule copie des frames, rendue en
/// teinte 0 : les minis recolorent leur noeud par la palette (indices 13 a 15), jamais
/// en regenerant les frames. C'est valable parce que seul le noeud utilise ces indices,
/// ce que SpriteGen verifie a chaque generation.
/// </summary>
internal sealed class SpriteLibrary
{
    public const int Size = PixelCanvas.Size;
    public const int MiniSize = PixelCanvas.Size / 2;
    public const int Baseline = PixelCanvas.Baseline;
    /// <summary>Ligne des pieds dans une frame de mini.</summary>
    public const int MiniBaseline = PixelCanvas.Baseline / 2;

    readonly Dictionary<string, RenderClip> _clips = new(StringComparer.Ordinal);
    readonly uint[][] _palettes;

    public SpriteLibrary()
    {
        foreach (var clip in Clips.BuildAll(0).Values)
        {
            _clips[clip.Name] = new RenderClip
            {
                Name = clip.Name,
                Frames = clip.Frames,
                Fps = clip.Fps,
                Loop = clip.Loop,
                LeavesBaseline = clip.LeavesBaseline,
                Mirrorable = clip.Mirrorable,
            };
        }

        var basePalette = Palette.Premultiplied();
        _palettes = new uint[Palette.BowHues.Length][];
        for (int h = 0; h < _palettes.Length; h++)
        {
            var p = (uint[])basePalette.Clone();
            var (dark, mid, light) = Palette.BowHues[h];
            p[Palette.BowDark] = basePalette[dark];
            p[Palette.BowMid] = basePalette[mid];
            p[Palette.BowLight] = basePalette[light];
            _palettes[h] = p;
        }
    }

    public RenderClip this[string name] => _clips[name];
    public bool Has(string name) => _clips.ContainsKey(name);
    public IEnumerable<RenderClip> All => _clips.Values;

    /// <summary>Palette ARGB premultipliee dont le noeud prend la teinte Palette.BowHues[hue].</summary>
    public uint[] PaletteFor(int hue) =>
        _palettes[((hue % _palettes.Length) + _palettes.Length) % _palettes.Length];
}
