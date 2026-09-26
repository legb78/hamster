using Hamster.Art;

namespace Hamster.App.Render;

/// <summary>Un clip pret a l'affichage : frames en ARGB premultiplie, plus leur miroir.</summary>
internal sealed class RenderClip
{
    public required string Name { get; init; }
    public required uint[][] Right { get; init; }
    public required uint[][] Left { get; init; }
    public required int Fps { get; init; }
    public required bool Loop { get; init; }
    public required bool LeavesBaseline { get; init; }

    public int FrameCount => Right.Length;
    public uint[] Frame(int index, bool facingLeft) => (facingLeft ? Left : Right)[index];
}

/// <summary>
/// Charge les clips. Phase 1 : tout est procedural. Des que Assets/sprites/ contient
/// des PNG, le loader les prendra en priorite et retombera sur le procedural pour
/// toute sheet manquante -- c'est ce qui permet de remplacer une animation a la fois.
/// </summary>
internal sealed class SpriteLibrary
{
    public const int Size = PixelCanvas.Size;
    public const int Baseline = PixelCanvas.Baseline;

    readonly Dictionary<string, RenderClip> _clips = new(StringComparer.Ordinal);

    public SpriteLibrary(int bowHue = 0)
    {
        var palette = Palette.Premultiplied();
        foreach (var clip in Clips.BuildAll(bowHue).Values)
        {
            var right = new uint[clip.FrameCount][];
            var left = new uint[clip.FrameCount][];
            for (int i = 0; i < clip.FrameCount; i++)
            {
                right[i] = PixelCanvas.ToArgb(clip.Frames[i], palette);
                left[i] = PixelCanvas.ToArgb(PixelCanvas.Mirror(clip.Frames[i]), palette);
            }
            _clips[clip.Name] = new RenderClip
            {
                Name = clip.Name,
                Right = right,
                Left = left,
                Fps = clip.Fps,
                Loop = clip.Loop,
                LeavesBaseline = clip.LeavesBaseline,
            };
        }
    }

    public RenderClip this[string name] => _clips[name];
    public bool Has(string name) => _clips.ContainsKey(name);
    public int MaxFps => _clips.Values.Max(c => c.Fps);
}
