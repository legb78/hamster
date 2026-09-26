namespace Hamster.Art;

/// <summary>Attente de l'utilisateur, fete de fin de tache, erreur.</summary>
public static partial class Clips
{
    /// <summary>
    /// WaitingUser : il a decroche et attend la reponse. Il regarde l'utilisateur, pas
    /// l'objet : c'est a lui qu'il parle. Le combine tenu contre la joue est la seule
    /// exception admise a la regle des objets poses au sol.
    /// </summary>
    static Clip BuildPhone(Kit k)
    {
        var mouth = new[] { Mouth.Droop, Mouth.Talk, Mouth.Droop, Mouth.Talk, Mouth.Talk, Mouth.Droop };
        int[] bounce = { 0, -1, -1, 0, 0, 0 };
        int[] bob    = { 0, 0, -1, 0, 0, -1 };
        var frames = new byte[mouth.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.OffsetX = PropShift;
            p.BodyDy = bob[i];
            p.Mouth = mouth[i];
            int f = i;
            int dy = bob[i];
            frames[i] = new Scene()
                .Layer(c => Props.SpeechBubble(c, bounce[f]))
                .Layer(Props.PhoneBase)
                .Hamster(p)
                .Layer(c => Props.Cord(c, f), outline: false)
                .Layer(c => Shifted(c, 0, dy, Props.Handset))
                .Layer(c => HamsterSprite.DrawPaw(c, 101, 82 + dy))
                .Shadow(115, 12)
                .Build();
        }
        return new Clip(Phone, frames, 6, true, false);
    }

    /// <summary>Dessine sur une couche temporaire puis la recopie decalee : pour faire suivre un objet au corps.</summary>
    static void Shifted(PixelCanvas c, int dx, int dy, Action<PixelCanvas> draw)
    {
        var tmp = new PixelCanvas();
        draw(tmp);
        for (int y = 0; y < PixelCanvas.Size; y++)
        for (int x = 0; x < PixelCanvas.Size; x++)
        {
            byte v = tmp.Px[y * PixelCanvas.Size + x];
            if (v != 0) c.Set(x + dx, y + dy, v);
        }
    }

    /// <summary>
    /// Fin de tache : deux sauts bras en l'air, feux d'artifice en haut du canvas,
    /// pluie de confettis. Tout l'effet passe DERRIERE le personnage : le visage reste net.
    /// </summary>
    static Clip BuildCelebrate(Kit k)
    {
        //               anticip.  saut 1                 recep. saut 2          recep.  pose
        int[] jump   = { 0, 3, -6, -14, -18, -14, -6, 2, 0, -8, -12, -8, 2, 0, -2, 0, 0, 0 };
        int[] squash = { 0, 2, -1,  -1,   0,   0, -1, 2, 1, -1,   0, -1, 2, 1,  0, 0, 0, 0 };
        (int X, int Y, int Start, byte Main, byte Second)[] bursts =
        {
            // dans les coins du haut : le centre est couvert par la tete au sommet du saut
            (16, 14, 2, Palette.AccentYell, Palette.AccentRed),
            (111, 13, 4, Palette.AccentBlue, Palette.EyeShine),
            (26, 10, 7, Palette.AccentGreen, Palette.AccentYell),
            (104, 16, 9, Palette.AccentPurp, Palette.AccentYell),
            (14, 18, 12, Palette.AccentRed, Palette.Paper),
            (112, 10, 13, Palette.AccentYell, Palette.AccentGreen),
        };

        var frames = new byte[jump.Length][];
        for (int i = 0; i < frames.Length; i++)
        {
            var p = k.Base();
            p.BodyDy = jump[i];
            p.Squash = squash[i];
            p.Airborne = jump[i] < 0;
            p.Mouth = i < 16 ? Mouth.Open : Mouth.Droop;
            int f = i;
            bool armsUp = i >= 2 && i <= 14;
            int wave = i % 2;
            int cy = HamsterSprite.CenterY + jump[i] + squash[i];
            frames[i] = new Scene()
                .Layer(c =>
                {
                    foreach (var b in bursts) Props.Firework(c, b.X, b.Y, f - b.Start, b.Main, b.Second);
                    Props.ConfettiRain(c, f, 3);
                }, outline: false)
                .Hamster(p)
                .Layer(c =>
                {
                    if (!armsUp) return;
                    // en haut des flancs, de part et d'autre de l'oreille et du noeud
                    HamsterSprite.DrawPaw(c, 64 - 51, cy - 25 - wave * 3);
                    HamsterSprite.DrawPaw(c, 64 + 51, cy - 19 - (1 - wave) * 3);
                })
                .Build();
        }
        return new Clip(Celebrate, frames, 6, false, true);
    }

    /// <summary>
    /// Erreur : tete catastrophee. Bouche carree, larmes au bord des yeux, lignes de
    /// deprime sur le crane, goutte de sueur, et un tremblement de 1 px.
    /// </summary>
    static Clip BuildError(Kit k)
    {
        const int n = 14;
        var frames = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            var p = k.Base();
            bool shaking = i >= 2 && i <= 11;
            p.HeadDx = shaking ? (i % 2 == 0 ? -1 : 1) : 0;
            p.Squash = i == 1 ? -2 : i == 0 ? 1 : 0;
            p.BodyDy = i == 1 ? -2 : 0;
            p.Mouth = i == 0 ? Mouth.Droop : i == 1 ? Mouth.Open : i <= 11 ? Mouth.Wail : Mouth.Droop;
            p.Tears = i >= 2 && i <= 12;
            p.EyeDy = i >= 2 && i <= 11 ? 1 : 0;
            int f = i;
            int cx = HamsterSprite.CenterX + p.HeadDx;
            int cy = HamsterSprite.CenterY + p.BodyDy + p.Squash;
            int gloom = i < 2 ? 0 : i <= 11 ? 5 : i == 12 ? 3 : 0;
            frames[i] = new Scene()
                .Hamster(p)
                .Layer(c => GloomLines(c, cx, cy, gloom), outline: false)
                .Layer(c =>
                {
                    if (f >= 2 && f <= 12) Props.SweatDrop(c, cx - 47, cy - 16 + (f - 2) / 3);
                    if (f >= 1 && f <= 11) c.Stamp(14 + p.HeadDx, f == 1 ? 9 : 5, Props.Bang, Palette.AccentRed);
                })
                .Build();
        }
        return new Clip(Error, frames, 7, false, false);
    }

    /// <summary>Lignes de deprime : traits verticaux en pointilles qui pendent du haut du crane.</summary>
    static void GloomLines(PixelCanvas c, int cx, int cy, int count)
    {
        int[] xs  = { -22, -15, -8, -1, 6 };
        int[] len = { 9, 14, 17, 14, 9 };
        for (int k = 0; k < Math.Min(count, xs.Length); k++)
        {
            int x = cx + xs[k];
            int top = cy - 42 + Math.Abs(xs[k] + 8) / 5;
            for (int y = top; y < top + len[k]; y++)
                if ((y - top) % 4 != 3) c.Set(x, y, Palette.AccentPurp);
        }
    }
}
