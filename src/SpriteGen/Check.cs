using Hamster.Art;

namespace Hamster.SpriteGen;

/// <summary>
/// Controles des regles de dessin, relances a chaque generation. Chacun rend une
/// liste de problemes lisibles ; vide = tout passe.
/// </summary>
internal static class Check
{
    const int MaxTotalFrames = 150;

    public static List<string> All(Dictionary<string, Clip> clips, Dictionary<string, Clip> withoutBow)
    {
        var problems = new List<string>();

        int total = clips.Values.Sum(c => c.FrameCount);
        if (total >= MaxTotalFrames) problems.Add($"budget : {total} frames, plafond {MaxTotalFrames}");

        var reference = HamsterSprite.Render(Pose.Default);
        int refEyes = PixelCanvas.Count(reference, Palette.EyeBlack);
        int refBow = BowPixels(reference);

        foreach (var clip in clips.Values)
        {
            if (clip.Fps < 5 || clip.Fps > 12) problems.Add($"{clip.Name} : {clip.Fps} fps, hors de [5, 12]");
            if (clip.Loop && (clip.FrameCount < 4 || clip.FrameCount > 10))
                problems.Add($"{clip.Name} : boucle de {clip.FrameCount} frames, hors de [4, 10]");

            bool fx = clip.Name.StartsWith("fx_", StringComparison.Ordinal);
            for (int f = 0; f < clip.FrameCount; f++)
            {
                var px = clip.Frames[f];
                string at = $"{clip.Name}[{f}]";

                foreach (byte b in px)
                    if (b >= Palette.Count) { problems.Add($"{at} : indice {b} hors palette"); break; }

                // le noeud masque, tout pixel 13 a 15 restant appartient a autre chose que le noeud
                int stray = BowPixels(withoutBow[clip.Name].Frames[f]);
                if (stray > 0) problems.Add($"{at} : {stray} pixel(s) 13-15 hors du noeud");

                if (!clip.LeavesBaseline)
                {
                    int bottom = LowestSolidRow(px);
                    if (bottom != PixelCanvas.Baseline)
                        problems.Add($"{at} : ligne de base a {bottom}, attendu {PixelCanvas.Baseline}");
                }

                if (fx) continue;

                int bow = BowPixels(px);
                if (bow * 2 < refBow) problems.Add($"{at} : noeud a {bow} px sur {refBow}, moins de la moitie");

                // blink ferme les yeux 1/12 s par construction : c'est le seul clip exempte
                int eyes = PixelCanvas.Count(px, Palette.EyeBlack);
                if (clip.Name != Clips.Blink && eyes * 4 < refEyes)
                    problems.Add($"{at} : yeux a {eyes} px sur {refEyes}, moins du quart");
            }
        }

        CheckDuration(problems, clips, Clips.Celebrate, 2.5, 3.5);
        CheckDuration(problems, clips, Clips.Error, 1.5, 2.5);
        return problems;
    }

    static void CheckDuration(List<string> problems, Dictionary<string, Clip> clips, string name, double min, double max)
    {
        if (!clips.TryGetValue(name, out var clip)) { problems.Add($"{name} : absent"); return; }
        if (clip.Loop) problems.Add($"{name} : doit etre un one-shot");
        if (clip.DurationSeconds < min || clip.DurationSeconds > max)
            problems.Add($"{name} : {clip.DurationSeconds:0.00} s, attendu entre {min} et {max}");
    }

    static int BowPixels(byte[] px)
    {
        int n = 0;
        foreach (byte b in px) if (b is Palette.BowDark or Palette.BowMid or Palette.BowLight) n++;
        return n;
    }

    /// <summary>Derniere ligne portant un pixel autre que transparent ou ombre : c'est la que les pieds se posent.</summary>
    static int LowestSolidRow(byte[] px)
    {
        for (int y = PixelCanvas.Size - 1; y >= 0; y--)
        for (int x = 0; x < PixelCanvas.Size; x++)
        {
            byte b = px[y * PixelCanvas.Size + x];
            if (b != Palette.Transparent && b != Palette.Shadow) return y;
        }
        return -1;
    }
}
