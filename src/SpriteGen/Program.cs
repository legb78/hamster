using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Hamster.Art;

namespace Hamster.SpriteGen;

/// <summary>
/// Rend toutes les sheets, la palette .gpl et un contact sheet de relecture.
/// Tout est procedural : chaque sheet pourra etre remplacee plus tard par une
/// version generee par modele d'image sans toucher au code de l'app.
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : FindRepoRoot();
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
            Console.Error.WriteLine($"sheet {clip.Name,-6} {clip.FrameCount} frames @ {clip.Fps} fps");
        }

        SaveContactSheet(clips.Values, Path.Combine(preview, "contact.png"));
        SaveHero(Path.Combine(preview, "hero.png"));
        SaveMiniComparison(Path.Combine(preview, "mini.png"));

        Console.Error.WriteLine("assets: " + assets);
        return 0;
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
        sb.Append($"  \"leavesBaseline\": {(clip.LeavesBaseline ? "true" : "false")}\n");
        sb.Append("}\n");
        File.WriteAllText(path, sb.ToString());
    }

    static void SaveContactSheet(IEnumerable<Clip> clips, string path)
    {
        const int s = PixelCanvas.Size, zoom = 2, pad = 6;
        var list = clips.ToList();
        int cols = list.Max(c => c.FrameCount);
        int w = pad + cols * (s * zoom + pad);
        int h = pad + list.Count * (s * zoom + pad);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Checkerboard(bmp);
        for (int r = 0; r < list.Count; r++)
        for (int f = 0; f < list[r].FrameCount; f++)
            BlitIndexed(bmp, list[r].Frames[f], pad + f * (s * zoom + pad), pad + r * (s * zoom + pad), zoom);

        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Consolas", 11))
        for (int r = 0; r < list.Count; r++)
            g.DrawString($"{list[r].Name}  {list[r].FrameCount}f @{list[r].Fps}fps",
                font, Brushes.Black, pad + 2, pad + r * (s * zoom + pad) + 2);

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

    static void SaveMiniComparison(string path)
    {
        const int s = PixelCanvas.Size;
        var full = HamsterSprite.Render(Pose.Default);
        using var bmp = new Bitmap(s * 2 + 6 * (s + 4), s * 2, PixelFormat.Format32bppArgb);
        Checkerboard(bmp);
        BlitIndexed(bmp, full, 0, 0, 2);
        for (int hue = 0; hue < Palette.BowHues.Length; hue++)
        {
            var p = Pose.Default;
            p.BowHue = hue;
            var mini = PixelCanvas.Downscale2x(HamsterSprite.Render(p));
            BlitIndexedSized(bmp, mini, s / 2, s * 2 + hue * (s + 4), s / 2, 2);
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
                    if ((argb >> 24) == 0) continue;
                    for (int zy = 0; zy < zoom; zy++)
                    for (int zx = 0; zx < zoom; zx++)
                    {
                        int px = dx + x * zoom + zx, py = dy + y * zoom + zy;
                        if ((uint)px < bmp.Width && (uint)py < bmp.Height)
                            baseP[py * stride + px] = argb;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
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
