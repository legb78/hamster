using Hamster.App.Render;
using Hamster.Art;

namespace Hamster.App.Tests;

static class RenderTests
{
    const int N = PixelCanvas.Size, M = N / 2;

    /// <summary>Clips qu'un mini peut jouer : les cinq scenes de travail, les humeurs, la reaction au clic.</summary>
    static readonly string[] MiniClips =
    {
        Clips.WorkLaptop, Clips.WorkBook, Clips.WorkTerminal, Clips.WorkLab, Clips.WorkConductor,
        Clips.Phone, Clips.Error, Clips.Celebrate, Clips.React,
    };

    public static void Run()
    {
        T.Suite("Rendu");
        var lib = Shared.Library;

        T.Case("frames indexees : 148 frames de 16 Kio, rien en ARGB", () =>
        {
            int frames = lib.All.Sum(c => c.FrameCount);
            long bytes = lib.All.Sum(c => c.Frames.Sum(f => (long)f.Length));
            T.Info($"{frames} frames, {bytes / 1024.0 / 1024.0:F2} Mio en indices (en ARGB avec miroir : {frames * N * N * 4 * 2 / 1024.0 / 1024.0:F1} Mio)");
            T.Eq((long)frames * N * N, bytes, "un octet par pixel");
        });

        T.Case("palette par teinte = remappage du noeud de Hamster.Art", () =>
        {
            var basePalette = Palette.Premultiplied();
            for (int h = 0; h < Palette.BowHues.Length; h++)
            {
                var table = Palette.BowRemap(h);
                var p = lib.PaletteFor(h);
                for (int i = 0; i < Palette.Count; i++)
                    T.Eq(basePalette[table[i]], p[i], $"teinte {h} indice {i}");
            }
        });

        T.Case("blit, miroir compris, identique a l'ancienne conversion ARGB", () =>
        {
            var comp = new Compositor();
            var palette = lib.PaletteFor(0);
            foreach (var clip in lib.All)
            foreach (var frame in clip.Frames)
            {
                foreach (bool mirror in new[] { false, true })
                {
                    comp.Reset(N, N);
                    comp.Blit(frame, N, 0, 0, palette, mirror);
                    var expected = PixelCanvas.ToArgb(mirror ? PixelCanvas.Mirror(frame) : frame, palette);
                    if (!comp.Pixels.AsSpan().SequenceEqual(expected)) throw new Exception($"{clip.Name} miroir={mirror}");
                }
            }
        });

        T.Case("mini : reduire puis retourner = retourner puis reduire", () =>
        {
            foreach (var clip in lib.All)
                for (int i = 0; i < clip.FrameCount; i++)
                {
                    var a = PixelCanvas.Downscale2x(PixelCanvas.Mirror(clip.Frames[i]));
                    var b = MirrorMini(clip.Mini(i));
                    if (!a.AsSpan().SequenceEqual(b)) throw new Exception(clip.Name + " " + i);
                }
        });

        T.Case("ombre translucide composee par-dessus, pas a la place", () =>
        {
            var comp = new Compositor();
            comp.Reset(1, 1);
            var palette = lib.PaletteFor(0);
            comp.Blit(new[] { Palette.FurMid }, 1, 0, 0, palette, false);
            comp.Blit(new[] { Palette.Shadow }, 1, 0, 0, palette, false);
            T.Eq(255u, comp.Pixels[0] >> 24, "reste opaque");
            T.True(comp.Pixels[0] != palette[Palette.FurMid], "assombri");
        });

        T.Case("agrandissement entier au plus proche voisin", () =>
        {
            var comp = new Compositor();
            comp.Reset(2, 1);
            comp.Fill(1, 0, 1, 1, 0xFF112233);
            var surface = new uint[8 * 3];
            comp.UpscaleInto(surface, 8, 1, 0, 3);
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 8; x++)
                T.Eq(x is >= 4 and < 7 ? 0xFF112233u : 0u, surface[y * 8 + x], $"pixel {x},{y}");
        });

        T.Case("police 3x5 : plus et chiffres", () =>
        {
            foreach (char c in "+0123456789") T.True(PixelFont.Has(c), "glyphe " + c);
            T.Eq(11, PixelFont.Measure("+12"), "largeur de +12");
            int lit = PixelFont.Draw("+8", 0, 0, (_, _) => { });
            T.Eq(5 + 13, lit, "pixels de +8");
        });

        T.Case("orbite : constantes", () =>
        {
            T.Eq(42, Orbit.Lift, "montee du principal");
            T.Eq(116, Orbit.HalfWidth, "demi-largeur");
        });

        T.Case("arc avant : le corps d'un mini au travail ne touche jamais le visage", () =>
        {
            var faces = lib.All.Where(c => !c.Name.StartsWith("fx_")).ToDictionary(c => c.Name, FaceUnion);
            var failures = new List<string>();
            foreach (var name in MiniClips)
            {
                var pixels = MiniUnion(lib[name]);
                int worst = 0;
                string worstMain = "";
                foreach (var (mainName, face) in faces)
                {
                    int n = WorstOverlap(pixels, face);
                    if (n > worst) { worst = n; worstMain = mainName; }
                }
                // au-dela du corps (bulle, "!", feux d'artifice, coeur), c'est le filet de securite qui protege
                T.Info($"{name,-15} {worst,4} px sur le visage au pire sans filet" + (worst > 0 ? $" (principal en {worstMain})" : ""));
                // les scenes de travail sont le cas courant des sous-agents : zero, sans filet de securite
                if (name.StartsWith("work_") && worst > 0) failures.Add(name);
            }
            T.True(failures.Count == 0, "recouvrement : " + string.Join(", ", failures));
        });

        T.Case("filet de securite : un mini de devant ne peint jamais sur le visage", () =>
        {
            var comp = new Compositor();
            var palette = lib.PaletteFor(0);
            int cases = 0;
            foreach (var main in lib.All.Where(c => !c.Name.StartsWith("fx_")))
            foreach (var mini in MiniClips.Select(n => lib[n]))
            for (int deg = 1; deg < 180; deg += 7)
            {
                int f = cases % main.FrameCount, g = cases % mini.FrameCount;
                var (dx, dy) = Orbit.At(deg * Math.PI / 180);
                // meme placement que PetController.Render, principal en (0, 0)
                int mx = N / 2 + dx - M / 2, my = SpriteLibrary.Baseline + dy - SpriteLibrary.MiniBaseline;
                comp.Reset(N, N + Orbit.Lift);
                comp.Blit(main.Frames[f], N, 0, 0, palette, false, markFace: main.Face(f));
                var alone = (uint[])comp.Pixels.Clone();
                comp.Blit(mini.Mini(g), M, mx, my, lib.PaletteFor(3), false, avoidFace: true);
                for (int i = 0; i < alone.Length; i++)
                    if (comp.Face[i] != 0 && comp.Pixels[i] != alone[i])
                        throw new Exception($"{mini.Name} sur le visage de {main.Name} a {deg} degres");
                cases++;
            }
            T.Info(cases + " compositions verifiees");
        });
    }

    static byte[] MirrorMini(byte[] src)
    {
        var o = new byte[src.Length];
        for (int y = 0; y < M; y++)
        for (int x = 0; x < M; x++)
            o[y * M + x] = src[y * M + (M - 1 - x)];
        return o;
    }

    /// <summary>Pixels du visage, toutes frames du clip, dans les deux sens s'il se retourne.</summary>
    static bool[] FaceUnion(RenderClip clip)
    {
        var mask = new bool[N * N];
        for (int f = 0; f < clip.FrameCount; f++)
        {
            if (clip.Face(f) is not { } box) continue;
            var frame = clip.Frames[f];
            for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                if ((uint)x >= N || (uint)y >= N || !box.Contains(x, y, frame[y * N + x])) continue;
                mask[y * N + x] = true;
                if (clip.Mirrorable) mask[y * N + (N - 1 - x)] = true;
            }
        }
        return mask;
    }

    /// <summary>Pixels opaques du clip reduit, toutes frames confondues. De face, un mini n'est jamais retourne.</summary>
    static List<(int X, int Y)> MiniUnion(RenderClip clip)
    {
        var seen = new bool[M * M];
        for (int f = 0; f < clip.FrameCount; f++)
        {
            var mini = clip.Mini(f);
            for (int i = 0; i < mini.Length; i++) if (mini[i] != 0) seen[i] = true;
        }
        var list = new List<(int, int)>();
        for (int i = 0; i < seen.Length; i++) if (seen[i]) list.Add((i % M, i / M));
        return list;
    }

    /// <summary>Pire recouvrement du visage sur tout l'arc avant, rebond compris.</summary>
    static int WorstOverlap(List<(int X, int Y)> mini, bool[] face)
    {
        int worst = 0;
        // pas d'un demi-degre : a Rx = 84, moins d'un pixel entre deux positions
        for (int half = 1; half < 360; half++)
        {
            var (dx, dy) = Orbit.At(half / 2.0 * Math.PI / 180);
            for (int bob = -Orbit.Bob; bob <= Orbit.Bob; bob++)
            {
                int ox = N / 2 + dx - M / 2, oy = SpriteLibrary.Baseline + dy + bob - SpriteLibrary.MiniBaseline;
                int n = 0;
                foreach (var (x, y) in mini)
                {
                    int fx = ox + x, fy = oy + y;
                    if ((uint)fx < N && (uint)fy < N && face[fy * N + fx]) n++;
                }
                if (n > worst) worst = n;
            }
        }
        return worst;
    }
}
