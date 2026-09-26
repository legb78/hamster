using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Hamster.Art;

namespace Hamster.SpriteGen;

/// <summary>
/// Rend toutes les sheets, la palette .gpl et les planches de relecture, puis controle
/// les regles de dessin. Tout est procedural : chaque sheet pourra etre remplacee plus
/// tard par une version generee par modele d'image sans toucher au code de l'app.
///
/// Usage : SpriteGen [racine] [--review dossier]
/// Code de sortie 1 si un controle echoue ; les fichiers sont ecrits quand meme, pour
/// qu'on puisse regarder ce qui cloche.
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        string? review = null;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--review" && i + 1 < args.Length) review = args[++i];
            else rest.Add(args[i]);
        }

        string root = rest.Count > 0 ? rest[0] : FindRepoRoot();
        string assets = Path.Combine(root, "Assets");
        string sprites = Path.Combine(assets, "sprites", "hamster");
        string preview = Path.Combine(assets, "preview");
        Directory.CreateDirectory(sprites);
        Directory.CreateDirectory(preview);

        WritePaletteGpl(Path.Combine(assets, "palette.gpl"));

        var clips = Clips.BuildAll();
        foreach (var clip in clips.Values)
        {
            SaveStrip(clip, Path.Combine(sprites, clip.Name + ".png"));
            SaveMeta(clip, Path.Combine(sprites, clip.Name + ".json"));
            Console.Error.WriteLine($"sheet {clip.Name,-14} {clip.FrameCount,2} frames @ {clip.Fps,2} fps  {clip.DurationSeconds:0.00} s{(clip.Loop ? "  boucle" : "")}");
        }
        Console.Error.WriteLine($"total : {clips.Values.Sum(c => c.FrameCount)} frames, {clips.Count} clips");

        SaveContactSheet(clips.Values, Path.Combine(preview, "contact.png"), zoom: 1);
        SaveHero(Path.Combine(preview, "hero.png"));
        SaveMiniComparison(clips, Path.Combine(preview, "mini.png"));

        if (review != null)
        {
            Directory.CreateDirectory(review);
            foreach (var clip in clips.Values)
                SaveContactSheet(new[] { clip }, Path.Combine(review, clip.Name + ".png"), zoom: 3, perRow: 4);
            Console.Error.WriteLine("relecture : " + review);
        }

        var problems = Check.All(clips, Clips.BuildAll(0, withBow: false));
        foreach (var p in problems) Console.Error.WriteLine("ECHEC " + p);
        Console.Error.WriteLine(problems.Count == 0 ? "controles : tous passes" : $"controles : {problems.Count} echec(s)");

        Console.Error.WriteLine("assets: " + assets);
        return problems.Count == 0 ? 0 : 1;
    }

    static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    // ---- sorties -----------------------------------------------------------

    static void SaveStrip(Clip clip, string path)
    {
        const int s = PixelCanvas.Size;
        using var bmp = new Bitmap(s * clip.FrameCount, s, PixelFormat.Format32bppArgb);
        for (int f = 0; f < clip.FrameCount; f++) BlitIndexed(bmp, clip.Frames[f], f * s, 0, 1);
        bmp.Save(path, ImageFormat.Png);
    }

    static void SaveMeta(Clip clip, string path)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"frameCount\": {clip.FrameCount},\n");
        sb.Append($"  \"fps\": {clip.Fps},\n");
        sb.Append($"  \"loop\": {(clip.Loop ? "true" : "false")},\n");
        sb.Append($"  \"frameSize\": [{PixelCanvas.Size}, {PixelCanvas.Size}],\n");
        sb.Append($"  \"pivot\": [{PixelCanvas.Size / 2}, {PixelCanvas.Baseline}],\n");
        sb.Append($"  \"leavesBaseline\": {(clip.LeavesBaseline ? "true" : "false")},\n");
        sb.Append($"  \"mirrorable\": {(clip.Mirrorable ? "true" : "false")}\n");
        sb.Append("}\n");
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Planche de contact. A 1x, c'est la taille reelle a l'echelle 1 : c'est a cette
    /// taille qu'un clip doit se lire. Les clips longs passent a la ligne.
    /// </summary>
    static void SaveContactSheet(IEnumerable<Clip> clips, string path, int zoom, int perRow = 12)
    {
        const int s = PixelCanvas.Size, pad = 6, label = 16;
        var list = clips.ToList();
        int cols = Math.Min(perRow, list.Max(c => c.FrameCount));
        int cell = s * zoom + pad;
        int rows = list.Sum(c => (c.FrameCount + perRow - 1) / perRow);
        int w = pad + cols * cell;
        int h = pad + rows * cell + list.Count * label;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Checkerboard(bmp);
        using var g = Graphics.FromImage(bmp);
        using var font = new Font("Consolas", 9);
        int y = pad;
        foreach (var clip in list)
        {
            string kind = clip.Loop ? "boucle" : "one-shot";
            g.DrawString($"{clip.Name}  {clip.FrameCount}f @{clip.Fps}fps  {clip.DurationSeconds:0.0}s  {kind}",
                font, Brushes.Black, pad, y);
            y += label;
            for (int f = 0; f < clip.FrameCount; f++)
            {
                int col = f % perRow;
                if (f > 0 && col == 0) y += cell;
                BlitIndexed(bmp, clip.Frames[f], pad + col * cell, y, zoom);
            }
            y += cell;
        }
        bmp.Save(path, ImageFormat.Png);
    }

    static void SaveHero(string path)
    {
        const int s = PixelCanvas.Size, zoom = 4;
        var pose = Pose.Default;
        using var bmp = new Bitmap(s * zoom, s * zoom, PixelFormat.Format32bppArgb);
        Checkerboard(bmp);
        BlitIndexed(bmp, HamsterSprite.Render(pose), 0, 0, zoom);
        bmp.Save(path, ImageFormat.Png);
    }

    /// <summary>
    /// Minis. Ligne 1 : le grand et ses six teintes de noeud. Ligne 2 : une frame de
    /// chaque clip d'etat, reduite de moitie a l'echelle 2, noeud recolore par table
    /// comme le fera l'app. Ligne 3 : les FX de minis, frame par frame.
    /// </summary>
    static void SaveMiniComparison(Dictionary<string, Clip> clips, string path)
    {
        const int s = PixelCanvas.Size, m = s / 2, gap = 4;
        string[] states =
        {
            Clips.Idle, Clips.WorkLaptop, Clips.WorkBook, Clips.WorkTerminal, Clips.WorkLab, Clips.WorkConductor,
            Clips.Think, Clips.Phone, Clips.Celebrate, Clips.Error,
            Clips.IdleConsole, Clips.IdleSnack, Clips.IdleSleep, Clips.IdleStretch, Clips.IdleWheel,
        };
        string[] fx = { Clips.FxPop, Clips.FxSparkle };
        int fxFrames = fx.Sum(n => clips[n].FrameCount);
        int w = Math.Max(s * 2 + 6 * (m * 2 + gap), Math.Max(states.Length, fxFrames + 1) * (m * 2 + gap));
        int h = s * 2 + (m * 2 + gap) * 2 + gap;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Checkerboard(bmp);
        BlitIndexed(bmp, HamsterSprite.Render(Pose.Default), 0, 0, 2);
        var still = HamsterSprite.Render(Pose.Default);
        for (int hue = 0; hue < Palette.BowHues.Length; hue++)
        {
            var mini = PixelCanvas.Downscale2x(PixelCanvas.Remap(still, Palette.BowRemap(hue)));
            BlitIndexedSized(bmp, mini, m, s * 2 + hue * (m * 2 + gap), m, 2);
        }

        int y = s * 2 + gap;
        for (int i = 0; i < states.Length; i++)
        {
            var clip = clips[states[i]];
            var frame = clip.Frames[Math.Min(clip.FrameCount - 1, clip.FrameCount / 3)];
            var mini = PixelCanvas.Downscale2x(PixelCanvas.Remap(frame, Palette.BowRemap(i % Palette.BowHues.Length)));
            BlitIndexedSized(bmp, mini, m, i * (m * 2 + gap), y, 2);
        }

        y += m * 2 + gap;
        int x = 0;
        foreach (var name in fx)
        {
            foreach (var frame in clips[name].Frames)
            {
                BlitIndexedSized(bmp, PixelCanvas.Downscale2x(frame), m, x, y, 2);
                x += m * 2 + gap;
            }
            x += gap * 4;
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // ---- primitives --------------------------------------------------------

    static void BlitIndexed(Bitmap bmp, byte[] indices, int dx, int dy, int zoom)
        => BlitIndexedSized(bmp, indices, PixelCanvas.Size, dx, dy, zoom);

    static void BlitIndexedSized(Bitmap bmp, byte[] indices, int size, int dx, int dy, int zoom)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                uint* baseP = (uint*)data.Scan0;
                int stride = data.Stride / 4;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    uint argb = Palette.Argb[indices[y * size + x]];
                    uint a = argb >> 24;
                    if (a == 0) continue;
                    for (int zy = 0; zy < zoom; zy++)
                    for (int zx = 0; zx < zoom; zx++)
                    {
                        int px = dx + x * zoom + zx, py = dy + y * zoom + zy;
                        if ((uint)px >= bmp.Width || (uint)py >= bmp.Height) continue;
                        // l'ombre est semi-transparente : on la compose sur le damier, sinon elle lit comme un trou
                        baseP[py * stride + px] = a == 255 ? argb : Blend(baseP[py * stride + px], argb);
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    static uint Blend(uint dst, uint src)
    {
        // sur fond transparent (les sheets), le pixel semi-transparent reste tel quel
        if ((dst >> 24) == 0) return src;
        uint a = src >> 24;
        uint r = (((src >> 16) & 0xFF) * a + ((dst >> 16) & 0xFF) * (255 - a)) / 255;
        uint g = (((src >> 8) & 0xFF) * a + ((dst >> 8) & 0xFF) * (255 - a)) / 255;
        uint b = ((src & 0xFF) * a + (dst & 0xFF) * (255 - a)) / 255;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    static void Checkerboard(Bitmap bmp)
    {
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 226, 226, 230));
        using var dark = new SolidBrush(Color.FromArgb(255, 205, 205, 212));
        for (int y = 0; y < bmp.Height; y += 8)
        for (int x = 0; x < bmp.Width; x += 8)
            if (((x / 8) + (y / 8)) % 2 == 0) g.FillRectangle(dark, x, y, 8, 8);
    }

    static void WritePaletteGpl(string path)
    {
        var sb = new StringBuilder();
        sb.Append("GIMP Palette\n");
        sb.Append("Name: Hamster 32\n");
        sb.Append("Columns: 8\n");
        sb.Append("#\n");
        for (int i = 0; i < Palette.Argb.Length; i++)
        {
            uint c = Palette.Argb[i];
            int r = (int)((c >> 16) & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)(c & 0xFF);
            sb.Append($"{r,3} {g,3} {b,3}\t{i:00} {Palette.Names[i]}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
