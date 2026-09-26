namespace Hamster.Art;

/// <summary>
/// FX des minis : one-shots sans personnage, sur le meme canvas 128 que les autres
/// clips, pour passer par le meme Downscale2x. Aucun indice du noeud : un FX ne se
/// recolore pas.
/// </summary>
public static partial class Clips
{
    /// <summary>Nuage de fumee : jaillit du sol, couvre la silhouette, se disperse en boules.</summary>
    static Clip BuildFxPop()
    {
        // (dx, dy, rayon) par frame, autour du centre du personnage
        (int X, int Y, int R)[][] puffs =
        {
            new[] { (0, 104, 8), (-9, 110, 6), (9, 110, 6) },
            new[] { (0, 92, 18), (-20, 104, 13), (20, 104, 13), (0, 110, 12) },
            new[] { (0, 76, 26), (-28, 92, 20), (28, 92, 20), (-14, 106, 16), (14, 106, 16), (0, 60, 16) },
            new[] { (-6, 70, 20), (-34, 88, 16), (34, 86, 16), (-18, 108, 12), (20, 108, 12), (6, 50, 12) },
            new[] { (-10, 62, 12), (-42, 84, 10), (42, 80, 10), (-26, 110, 8), (28, 110, 8), (10, 40, 8) },
            new[] { (-12, 56, 5), (-48, 80, 4), (48, 74, 4), (-32, 112, 3), (34, 112, 3), (12, 32, 3) },
        };
        var frames = new byte[puffs.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var set = puffs[i];
            frames[i] = new Scene()
                .Layer(c => { foreach (var q in set) Props.Puff(c, 64 + q.X, Math.Min(q.Y, 120 - q.R), q.R); })
                .Build();
        }
        return new Clip(FxPop, frames, 12, false, true);
    }

    /// <summary>
    /// Etincelles : un eclair au centre, un anneau blanc qui s'ouvre, puis deux couronnes
    /// de croix jaunes qui s'ecartent, grandissent et s'eteignent. Jaune et blanc : lisibles
    /// sur un fond clair comme sur un fond sombre.
    /// </summary>
    static Clip BuildFxSparkle()
    {
        const int n = 6, count = 10;
        const int cx = 64, cy = 80;
        int[] radius = { 0, 18, 28, 36, 42, 46 };
        int[] size   = { 0, 2, 4, 3, 2, 0 };
        var frames = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            int f = i;
            frames[i] = new Scene()
                .Layer(c =>
                {
                    if (f == 0) { Props.Sparkle(c, cx, cy, 7, Palette.AccentYell); c.Ring(cx, cy, 10, 9, 1, Palette.EyeShine); }
                    if (f == 1) c.Ring(cx, cy, 20, 17, 1, Palette.EyeShine);
                    if (f == 0) return;
                    for (int s = 0; s < count; s++)
                    {
                        // la seconde couronne a une frame de retard et un demi-pas d'angle
                        bool late = s % 2 == 1;
                        int age = late ? f - 1 : f;
                        if (age < 1) continue;
                        double a = s * (Math.PI * 2 / count) + 0.2;
                        double r = radius[age] - (late ? 6 : 0);
                        int x = cx + (int)Math.Round(Math.Cos(a) * r);
                        int y = cy + (int)Math.Round(Math.Sin(a) * r * 0.85);
                        Props.Sparkle(c, x, y, size[age] - (late ? 1 : 0), Palette.AccentYell);
                    }
                }, outline: false)
                .Build();
        }
        return new Clip(FxSparkle, frames, 12, false, true);
    }
}
