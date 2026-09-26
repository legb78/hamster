namespace Hamster.Art;

/// <summary>
/// Compose une frame en couches, du fond vers l'avant. Chaque couche recoit son propre
/// contour : c'est ce qui detache un objet pose devant le personnage, la ou un contour
/// unique fondrait les deux en une seule silhouette illisible.
/// </summary>
internal sealed class Scene
{
    readonly PixelCanvas _out = new();
    readonly List<Action<PixelCanvas>> _shadows = new();

    public Scene Layer(Action<PixelCanvas> draw, bool outline = true)
    {
        var layer = new PixelCanvas();
        draw(layer);
        if (outline) layer.OutlineSilhouette(Palette.Outline);
        _out.Over(layer);
        return this;
    }

    public Scene Hamster(Pose pose)
    {
        Layer(c => HamsterSprite.DrawBody(c, pose));
        if (pose.Shadow) _shadows.Add(c => HamsterSprite.DrawShadow(c, pose));
        return this;
    }

    /// <summary>Ombre d'un objet pose au sol. Toutes les ombres passent sous le reste, a la fin.</summary>
    public Scene Shadow(int cx, int rx)
    {
        _shadows.Add(c => c.EllipseUnder(cx, PixelCanvas.Baseline + 2, rx, 3, Palette.Shadow));
        return this;
    }

    public byte[] Build()
    {
        foreach (var s in _shadows) s(_out);
        return _out.Snapshot();
    }
}
