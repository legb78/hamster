namespace Hamster.Art;

/// <summary>
/// Les objets des scenes. Coordonnees absolues sur le canvas 128 : les scenes a objet
/// decalent le personnage de 12 px vers la gauche, l'objet occupe la bande de droite.
/// Regles tenues ici : tout objet pose au sol finit a la ligne 120 (son contour tombe
/// sur la ligne de base 121), et aucun n'utilise les indices 13 a 15, reserves au noeud.
/// </summary>
internal static class Props
{
    public static readonly string[] Question =
    {
        ".####.",
        "##..##",
        "....##",
        "...##.",
        "..##..",
        "..##..",
        "......",
        "..##..",
        "..##..",
    };

    public static readonly string[] Bang =
    {
        ".###.",
        "#####",
        "#####",
        "#####",
        ".###.",
        ".###.",
        ".###.",
        "..#..",
        "..#..",
        ".....",
        ".###.",
        "#####",
        ".###.",
    };

    public static readonly string[] Note =
    {
        "..###",
        "..#.#",
        "..#..",
        "..#..",
        "###..",
        "###..",
    };

    static readonly string[][] Zs =
    {
        new[] { "###", "..#", ".#.", "#..", "###" },
        new[] { "#####", "...#.", "..#..", ".#...", "#####" },
        new[] { "#######", ".....#.", "....#..", "...#...", "..#....", ".#.....", "#######" },
    };

    /// <summary>Sticker a liseret blanc : sans lui, un sticker sombre disparait sur le metal.</summary>
    static void Sticker(PixelCanvas c, int x, int y, string[] rows, byte ink)
    {
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
            c.Stamp(x + ox, y + oy, rows, Palette.Paper);
        c.Stamp(x, y, rows, ink);
    }

    // ---- laptop -------------------------------------------------------------

    /// <summary>
    /// Laptop vu de trois quarts arriere : l'ecran est tourne vers le hamster, on voit
    /// le dos du couvercle et ses stickers, et le clavier qui depasse vers lui.
    /// </summary>
    public static void Laptop(PixelCanvas c)
    {
        // socle : une dalle mince posee au sol
        c.Polygon(Palette.Metal, (70, 115), (116, 115), (118, 120), (68, 120));
        c.HLine(68, 118, 120, Palette.MetalDark);
        c.HLine(71, 115, 115, Palette.MetalLight);
        for (int x = 72; x <= 86; x += 3) { c.Set(x, 117, Palette.MetalDark); c.Set(x + 1, 117, Palette.MetalDark); }
        for (int x = 73; x <= 85; x += 3) { c.Set(x, 118, Palette.MetalDark); c.Set(x + 1, 118, Palette.MetalDark); }

        // couvercle, incline vers l'arriere
        c.Polygon(Palette.Metal, (88, 115), (114, 115), (123, 86), (97, 86));
        c.Line(114, 115, 123, 86, Palette.MetalDark);
        c.Line(113, 115, 122, 86, Palette.MetalDark);
        c.HLine(97, 122, 86, Palette.MetalLight);
        // l'ecran allume se devine sur la tranche tournee vers le hamster
        c.Line(88, 115, 97, 86, Palette.AccentBlue);
        c.HLine(88, 114, 114, Palette.MetalDark);

        Sticker(c, 101, 91, new[] { ".rr.", "rrrr", "rrrr", ".rr." }, Palette.AccentRed);
        Sticker(c, 110, 93, new[] { "..yy", ".yy.", "yyyy", ".yy.", "yy.." }, Palette.AccentYell);
        Sticker(c, 97, 101, new[] { "#####", "#####", "#####", "#####" }, Palette.AccentBlue);
        foreach (var (px, py) in new[] { (98, 102), (100, 102), (98, 104), (100, 104) }) c.Set(px, py, Palette.EyeShine);
        Sticker(c, 106, 104, new[] { ".gg.", "gggg", "g..g" }, Palette.AccentGreen);
        Sticker(c, 99, 108, new[] { "v.v", "vvv", ".v." }, Palette.AccentPurp);
    }

    // ---- livres -------------------------------------------------------------

    static void BookSpine(PixelCanvas c, int x, int y, int w, int h, byte cover)
    {
        c.HLine(x, x + w - 1, y, Palette.Outline);           // separation avec le livre du dessous
        c.Rect(x, y + 1, w, h - 1, cover);
        c.Rect(x + w - 4, y + 2, 3, h - 3, Palette.Paper);   // tranche des pages
        for (int j = y + 3; j < y + h - 1; j += 2) c.HLine(x + w - 4, x + w - 2, j, Palette.PaperShade);
        c.VLine(x + 3, y + 1, y + h - 1, Palette.AccentYell); // bandes du titre
        c.VLine(x + 6, y + 1, y + h - 1, Palette.AccentYell);
    }

    /// <summary>Pile de livres, a droite, derriere le livre ouvert.</summary>
    public static void BookStack(PixelCanvas c)
    {
        BookSpine(c, 103, 96, 18, 6, Palette.AccentPurp);
        BookSpine(c, 99, 102, 23, 6, Palette.AccentGreen);
        BookSpine(c, 101, 108, 23, 6, Palette.AccentRed);
        BookSpine(c, 98, 114, 27, 7, Palette.AccentBlue);
    }

    /// <summary>Gros livre ouvert pose a plat : deux pages bombees, la reliure dessous.</summary>
    public static void OpenBook(PixelCanvas c)
    {
        c.Polygon(Palette.WoodDark, (63, 116), (111, 116), (112, 120), (62, 120));
        c.Polygon(Palette.Paper, (64, 111), (76, 106), (86, 107), (87, 117), (64, 117));
        c.Polygon(Palette.Paper, (88, 107), (98, 106), (110, 111), (110, 117), (88, 117));
        // ombre de la reliure et tranches des pages
        c.VLine(87, 108, 117, Palette.PaperShade);
        c.VLine(88, 108, 117, Palette.PaperShade);
        c.HLine(64, 86, 117, Palette.PaperShade);
        c.HLine(89, 110, 117, Palette.PaperShade);
        for (int j = 0; j < 3; j++)
        {
            int y = 110 + j * 2;
            c.HLine(69 + (j == 0 ? 3 : 0), 75, y, Palette.PaperShade);
            c.HLine(77, 84, y, Palette.PaperShade);
            c.HLine(91, 98, y, Palette.PaperShade);
            c.HLine(100, 106 - (j == 0 ? 3 : 0), y, Palette.PaperShade);
        }
    }

    /// <summary>Page qui tourne. step 0 = soulevee, 1 = debout sur la reliure, 2 = retombant a gauche.</summary>
    public static void TurningPage(PixelCanvas c, int step)
    {
        switch (step)
        {
            case 0: c.Polygon(Palette.Paper, (88, 111), (99, 103), (106, 106), (89, 116)); break;
            case 1: c.Polygon(Palette.Paper, (86, 97), (89, 97), (89, 115), (86, 115)); break;
            default: c.Polygon(Palette.Paper, (87, 111), (76, 103), (69, 106), (86, 116)); break;
        }
    }

    /// <summary>Loupe : verre bleute, reflet, manche en bois qui descend vers la main, en bas a gauche.</summary>
    public static void Magnifier(PixelCanvas c, int x, int y)
    {
        for (int i = 0; i < 7; i++)
        {
            c.Set(x - 5 - i, y + 4 + i, Palette.Wood);
            c.Set(x - 6 - i, y + 4 + i, Palette.WoodDark);
            c.Set(x - 5 - i, y + 5 + i, Palette.WoodDark);
        }
        c.Ellipse(x, y, 6, 6, Palette.Metal);
        c.Ellipse(x, y, 4, 4, Palette.EyeShine2);
        c.Line(x - 2, y - 1, x, y - 3, Palette.EyeShine);
        c.Set(x + 2, y + 2, Palette.EyeShine);
    }

    // ---- terminal -----------------------------------------------------------

    // quatre lignes de "code", repetees : la periode divise la boucle, le defilement reste continu
    static readonly int[][] CodeLines =
    {
        new[] { 3, 5, 2, 6 },
        new[] { 2, 4, 7 },
        new[] { 5, 3, 4, 2, 2 },
        new[] { 4, 8, 3 },
    };

    /// <summary>Moniteur a tube : ecran noir, texte vert qui defile, curseur qui clignote.</summary>
    public static void Terminal(PixelCanvas c, int frame)
    {
        c.Rect(93, 81, 33, 33, Palette.Metal);
        c.HLine(94, 124, 82, Palette.MetalLight);
        c.VLine(94, 82, 112, Palette.MetalLight);
        c.Rect(96, 85, 27, 23, Palette.MetalDark);
        c.Rect(97, 86, 25, 21, Palette.ScreenDark);
        c.HLine(97, 103, 110, Palette.MetalDark);
        c.HLine(97, 103, 112, Palette.MetalDark);
        c.Rect(119, 110, 2, 2, Palette.ScreenGreen);
        // pied
        c.Rect(104, 114, 11, 3, Palette.MetalDark);
        c.Rect(99, 117, 21, 4, Palette.Metal);
        c.HLine(99, 119, 117, Palette.MetalLight);

        int scroll = frame / 2;
        const int rows = 6;
        for (int i = 0; i < rows; i++)
        {
            var words = CodeLines[(i + scroll) % CodeLines.Length];
            int y = 88 + i * 3;
            int x = i % 2 == 1 ? 102 : 99;   // indentation une ligne sur deux
            int budget = int.MaxValue;
            // la derniere ligne est en train d'etre tapee
            if (i == rows - 1) budget = frame % 2 == 0 ? 4 : 9;
            foreach (int w in words)
            {
                int len = Math.Min(w, budget);
                if (len <= 0 || x + len > 120) break;
                c.HLine(x, x + len - 1, y, Palette.ScreenGreen);
                x += len + 1;
                budget -= w + 1;
            }
            if (i == rows - 1 && frame % 2 == 0) c.Rect(Math.Min(x, 119), y - 1, 2, 2, Palette.ScreenGreen);
        }
    }

    /// <summary>Clavier plat pose au sol devant le personnage.</summary>
    public static void Keyboard(PixelCanvas c)
    {
        c.Polygon(Palette.Metal, (68, 116), (95, 116), (97, 120), (66, 120));
        c.HLine(66, 97, 120, Palette.MetalDark);
        for (int x = 69; x <= 93; x += 2) c.Set(x, 117, Palette.MetalLight);
        for (int x = 70; x <= 94; x += 2) c.Set(x, 118, Palette.MetalLight);
    }

    // ---- labo -----------------------------------------------------------------

    // Glyphes 3x5 (m en 5x5) pour la craie. Assembles par Word, une colonne vide entre deux.
    static readonly string[] GE  = { "###", "#..", "##.", "#..", "###" };
    static readonly string[] GEq = { "...", "###", "...", "###", "..." };
    static readonly string[] GM  = { ".....", "####.", "#.#.#", "#.#.#", "#.#.#" };
    static readonly string[] GC  = { "...", ".##", "#..", "#..", ".##" };
    static readonly string[] G2  = { "##.", "..#", ".#.", "###", "..." };
    static readonly string[] GX  = { "...", "#.#", ".#.", "#.#", "..." };
    static readonly string[] GPl = { "...", ".#.", "###", ".#.", "..." };
    static readonly string[] G1  = { ".#.", "##.", ".#.", ".#.", "###" };

    static string[] Word(params string[][] glyphs)
    {
        var rows = new string[5];
        for (int r = 0; r < 5; r++) rows[r] = string.Join(".", glyphs.Select(g => g[r]));
        return rows;
    }

    // compacts, pour tenir dans la partie du tableau que le personnage ne cache pas
    static readonly string[] EqMc2 = Word(GE, GEq, GM, GC, G2);
    static readonly string[] Formula2 = Word(GX, G2, GPl, G1);

    /// <summary>Tableau noir sur chevalet, derriere le personnage : E=mc2 et une courbe a la craie.</summary>
    public static void Blackboard(PixelCanvas c)
    {
        c.VLine(99, 80, 120, Palette.WoodDark);
        c.VLine(100, 80, 120, Palette.WoodDark);
        c.VLine(122, 80, 120, Palette.WoodDark);
        c.VLine(123, 80, 120, Palette.WoodDark);
        c.Rect(92, 42, 35, 40, Palette.WoodDark);
        c.Rect(94, 44, 31, 36, Palette.ScreenDark);
        c.Stamp(104, 47, EqMc2, Palette.Paper);
        c.Stamp(105, 56, Formula2, Palette.Paper);
        // une parabole
        for (int x = 0; x <= 14; x++) c.Set(104 + x, 76 - (x - 7) * (x - 7) / 5, Palette.Paper);
        c.HLine(103, 120, 76, Palette.PaperShade);
    }

    /// <summary>Support d'eprouvettes pose au sol, trois tubes dont le liquide bulle.</summary>
    public static void TestTubes(PixelCanvas c, int frame)
    {
        // le support, derriere les tubes
        c.Rect(90, 118, 32, 3, Palette.Wood);
        c.Rect(91, 110, 2, 8, Palette.WoodDark);
        c.Rect(119, 110, 2, 8, Palette.WoodDark);

        byte[] liquid = { Palette.AccentGreen, Palette.AccentPurp, Palette.AccentBlue };
        int[] level = { 104, 108, 106 };
        for (int t = 0; t < 3; t++)
        {
            int x = 96 + t * 9;
            c.Rect(x, 94, 5, 22, Palette.EyeShine2);
            c.Ellipse(x + 2, 115, 2, 2, Palette.EyeShine2);
            c.Rect(x, level[t], 5, 116 - level[t], liquid[t]);
            c.Ellipse(x + 2, 115, 2, 2, liquid[t]);
            c.VLine(x + 1, 95, level[t] - 1, Palette.EyeShine);
            // bulles qui montent : chaque tube a sa phase
            for (int b = 0; b < 2; b++)
            {
                int age = (frame + t * 2 + b * 3) % 6;
                int by = level[t] + 6 - age * 4;
                if (by < 88) continue;
                c.Set(x + 1 + (age + b) % 3, by, Palette.EyeShine);
                if (by < 94) c.Set(x + 2 + (age + b) % 3, by - 1, Palette.EyeShine);
            }
        }
        // barre avant du support, devant les tubes
        c.Rect(90, 109, 32, 2, Palette.Wood);
        c.HLine(90, 121, 109, Palette.WoodDark);
    }

    // ---- chef d'orchestre ------------------------------------------------------

    /// <summary>Les trois cases de l'organigramme, dans l'ordre ou la baguette les designe.</summary>
    public static readonly (int X, int Y, byte Col)[] Boxes =
    {
        (98, 46, Palette.AccentBlue),
        (112, 60, Palette.AccentGreen),
        (98, 72, Palette.AccentRed),
    };

    /// <summary>Tableau blanc sur pied, avec un organigramme : lit comme "orchestrer" a 1x.</summary>
    public static void Whiteboard(PixelCanvas c, int lit)
    {
        c.VLine(100, 88, 120, Palette.MetalDark);
        c.VLine(120, 88, 120, Palette.MetalDark);
        c.HLine(97, 123, 120, Palette.MetalDark);
        c.Rect(92, 38, 35, 50, Palette.MetalLight);
        c.Rect(94, 40, 31, 46, Palette.EyeShine);
        c.HLine(95, 124, 86, Palette.Metal);

        // fleches entre les cases
        c.VLine(103, 52, 71, Palette.Outline);
        c.HLine(104, 111, 49, Palette.Outline);
        c.VLine(117, 52, 59, Palette.Outline);
        c.Set(116, 58, Palette.Outline);
        c.Set(118, 58, Palette.Outline);
        c.HLine(104, 115, 75, Palette.Outline);
        c.VLine(117, 66, 74, Palette.Outline);
        c.Set(102, 70, Palette.Outline);
        c.Set(104, 70, Palette.Outline);

        for (int i = 0; i < Boxes.Length; i++)
        {
            var (x, y, col) = Boxes[i];
            c.Rect(x, y, 11, 7, col);
            c.Rect(x + 2, y + 2, 7, 3, i == lit ? Palette.AccentYell : Palette.EyeShine);
        }
    }

    /// <summary>Baguette : du poing vers la cible, bout blanc, et un trait de vitesse si elle bouge.</summary>
    public static void Baton(PixelCanvas c, int x0, int y0, int tx, int ty, bool swish)
    {
        // la baguette va jusqu'a la case : plus courte, elle designait la mauvaise
        int x1 = tx, y1 = ty;
        c.Line(x0, y0, x1, y1, Palette.WoodDark);
        c.Line(x0 + 1, y0, x1 + 1, y1, Palette.WoodDark);
        c.Rect(x1, y1 - 1, 2, 2, Palette.EyeShine);
        if (swish)
        {
            c.Line(x1 - 4, y1 + 4, x1 - 8, y1 + 7, Palette.Outline);
            c.Line(x1 - 1, y1 + 6, x1 - 4, y1 + 10, Palette.Outline);
        }
    }

    // ---- skate ------------------------------------------------------------------

    /// <summary>Skate : planche a spatules, trucks, roues jaunes dont un rayon tourne.</summary>
    public static void Skateboard(PixelCanvas c, int frame)
    {
        c.Rect(32, 110, 66, 3, Palette.Wood);
        c.HLine(32, 97, 112, Palette.WoodDark);
        c.Line(28, 106, 32, 110, Palette.Wood);
        c.Line(29, 106, 33, 110, Palette.Wood);
        c.Line(97, 110, 101, 106, Palette.Wood);
        c.Line(96, 110, 100, 106, Palette.Wood);
        foreach (int wx in new[] { 44, 86 })
        {
            c.Rect(wx - 4, 113, 9, 2, Palette.MetalDark);
            c.Ellipse(wx, 117, 3, 3, Palette.AccentYell);
            double a = frame * Math.PI / 2;
            c.Set(wx + (int)Math.Round(Math.Cos(a) * 2), 117 + (int)Math.Round(Math.Sin(a) * 2), Palette.WoodDark);
        }
    }

    /// <summary>Traits de vitesse derriere le personnage, qui filent vers la gauche.</summary>
    public static void SpeedLines(PixelCanvas c, int frame)
    {
        int[] ys = { 62, 78, 94 };
        for (int i = 0; i < ys.Length; i++)
        {
            int x = 10 - (frame * 3 + i * 5) % 10;
            c.HLine(x, x + 6 + i % 2 * 3, ys[i], Palette.Metal);
        }
    }

    // ---- telephone ----------------------------------------------------------

    /// <summary>Combine tenu contre la joue : ecouteur en haut, micro en bas, poignee bombee.</summary>
    public static void Handset(PixelCanvas c)
    {
        c.Polygon(Palette.AccentRed, (95, 69), (101, 71), (100, 92), (94, 92));
        c.Ellipse(95, 67, 5, 4, Palette.AccentRed);
        c.Ellipse(93, 94, 5, 4, Palette.AccentRed);
        c.VLine(99, 74, 86, Palette.EyeShine);
    }

    /// <summary>Cordon en spirale, du combine jusqu'au socle.</summary>
    public static void Cord(PixelCanvas c, int phase)
    {
        const int x0 = 94, y0 = 99, x1 = 108, y1 = 108;
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            double wob = Math.Sin((t * 5 + phase * 0.25) * Math.PI * 2) * 2;
            int x = (int)Math.Round(x0 + (x1 - x0) * t + wob);
            int y = (int)Math.Round(y0 + (y1 - y0) * t - wob * 0.5);
            c.Set(x, y, Palette.Outline);
        }
    }

    /// <summary>Socle de telephone a cadran, fourche vide : le combine est decroche.</summary>
    public static void PhoneBase(PixelCanvas c)
    {
        c.Polygon(Palette.AccentRed, (104, 120), (125, 120), (122, 109), (107, 109));
        c.Rect(106, 106, 3, 4, Palette.AccentRed);
        c.Rect(120, 106, 3, 4, Palette.AccentRed);
        c.HLine(107, 121, 110, Palette.EyeShine);
        c.Ellipse(114, 115, 4, 3, Palette.Paper);
        c.Set(111, 114, Palette.MetalDark);
        c.Set(114, 113, Palette.MetalDark);
        c.Set(117, 114, Palette.MetalDark);
        c.Set(112, 117, Palette.MetalDark);
        c.Set(116, 117, Palette.MetalDark);
        c.Set(114, 115, Palette.AccentRed);
    }

    /// <summary>Bulle de dialogue, queue vers le bas a droite, avec un point d'interrogation.</summary>
    public static void SpeechBubble(PixelCanvas c, int bounce)
    {
        c.Ellipse(19, 15, 15, 11, Palette.EyeShine);
        c.Polygon(Palette.EyeShine, (24, 22), (31, 22), (34, 31));
        c.Stamp(16, 10 + bounce, Question, Palette.AccentBlue);
    }

    // ---- reflexion ----------------------------------------------------------

    /// <summary>Bulle de pensee en haut a gauche, loin du noeud, avec 1 a 3 points.</summary>
    public static void ThoughtBubble(PixelCanvas c)
    {
        c.Ellipse(16, 14, 9, 8, Palette.EyeShine);
        c.Ellipse(28, 10, 10, 8, Palette.EyeShine);
        c.Ellipse(38, 15, 7, 7, Palette.EyeShine);
        c.Ellipse(22, 20, 8, 6, Palette.EyeShine);
        c.Ellipse(33, 20, 7, 5, Palette.EyeShine);
    }

    public static void ThoughtDots(PixelCanvas c, int dots)
    {
        for (int i = 0; i < dots; i++) c.Rect(18 + i * 7, 14, 3, 3, Palette.Outline);
    }

    public static void ThoughtTrail(PixelCanvas c)
    {
        c.Ellipse(45, 28, 3, 2, Palette.EyeShine);
        c.Ellipse(51, 33, 1, 1, Palette.EyeShine);
    }

    // ---- repos ----------------------------------------------------------------

    /// <summary>Console portable debout au sol, ecran vert, croix et boutons.</summary>
    public static void GameConsole(PixelCanvas c, int frame)
    {
        c.Rect(92, 93, 18, 28, Palette.AccentPurp);
        c.Set(92, 93, Palette.Transparent);
        c.Set(109, 93, Palette.Transparent);
        c.HLine(107, 109, 120, Palette.Transparent);
        c.VLine(109, 118, 120, Palette.Transparent);
        c.Set(108, 119, Palette.Transparent);
        c.Rect(94, 95, 14, 12, Palette.MetalDark);
        c.Rect(96, 97, 10, 8, Palette.ScreenGreen);
        // le petit personnage du jeu saute sur place
        int hop = frame % 3 == 1 ? 1 : 0;
        c.HLine(96, 105, 103, Palette.ScreenDark);
        c.Rect(98 + frame % 3 * 2, 100 - hop, 2, 2, Palette.ScreenDark);
        c.Set(103, 99, Palette.ScreenDark);
        // croix
        c.Rect(95, 112, 5, 1, Palette.Outline);
        c.Rect(97, 110, 1, 5, Palette.Outline);
        // boutons A et B
        c.Ellipse(103, 113, 1, 1, Palette.AccentRed);
        c.Ellipse(106, 111, 1, 1, Palette.AccentRed);
        c.Line(101, 118, 103, 116, Palette.MetalDark);
        c.Line(103, 118, 105, 116, Palette.MetalDark);
    }

    /// <summary>Graines de tournesol : ovales bruns rayes de clair.</summary>
    public static void Seed(PixelCanvas c, int x, int y)
    {
        c.Ellipse(x, y, 3, 2, Palette.WoodDark);
        c.HLine(x - 1, x + 1, y, Palette.Paper);
    }

    /// <summary>
    /// Bol de graines. Un tas brun pose a meme le sol pretait a confusion a 1x ;
    /// dans un bol, c'est a manger.
    /// </summary>
    public static void SeedBowl(PixelCanvas c, int taken)
    {
        Seed(c, 107, 110);
        Seed(c, 113, 109);
        Seed(c, 119, 110);
        if (taken < 1) Seed(c, 110, 107);
        Seed(c, 116, 106);
        // le bol : demi-ellipse basse, bord clair
        for (int dy = 0; dy <= 8; dy++)
        {
            int half = (int)(12 * Math.Sqrt(1 - dy * dy / 81.0) + 0.5);
            c.HLine(113 - half, 113 + half, 112 + dy, Palette.AccentBlue);
        }
        c.HLine(101, 125, 112, Palette.MetalLight);
        c.HLine(104, 110, 115, Palette.EyeShine2);
    }

    public static void Z(PixelCanvas c, int x, int y, int size) => c.Stamp(x, y, Zs[Math.Clamp(size, 0, 2)], Palette.EyeShine2);

    // ---- fete ---------------------------------------------------------------

    static readonly byte[] Confetti =
    {
        Palette.AccentRed, Palette.AccentBlue, Palette.AccentYell,
        Palette.AccentGreen, Palette.AccentPurp, Palette.Paper,
    };

    /// <summary>Confettis qui tombent en tournant. Deterministe : meme frame, meme dessin.</summary>
    public static void ConfettiRain(PixelCanvas c, int frame, int start)
    {
        if (frame < start) return;
        for (int i = 0; i < 30; i++)
        {
            int delay = (i * 7) % 5;
            int t = frame - start - delay;
            if (t < 0) continue;
            int speed = 5 + (i * 3) % 4;
            int y = -2 + t * speed - (i * 11) % 20;
            if (y < 0 || y > 118) continue;
            int x = (i * 37 + 9) % 124 + (int)Math.Round(Math.Sin((t + i) * 1.3) * 2);
            byte col = Confetti[i % Confetti.Length];
            switch ((t + i) % 3)
            {
                case 0: c.Rect(x, y, 3, 2, col); break;
                case 1: c.Rect(x + 1, y - 1, 2, 3, col); break;
                default: c.Rect(x, y, 2, 2, col); break;
            }
        }
    }

    /// <summary>
    /// Feu d'artifice. age 0 = fusee qui monte, 1 = eclair, 2 a 4 = gerbe qui s'ouvre
    /// puis s'eteint en points. Au-dela : rien.
    /// </summary>
    public static void Firework(PixelCanvas c, int x, int y, int age, byte main, byte second)
    {
        switch (age)
        {
            case 0:
                c.VLine(x, y + 8, y + 12, Palette.AccentYell);
                c.Set(x, y + 7, Palette.EyeShine);
                return;
            case 1:
                c.Rect(x - 1, y - 1, 3, 3, Palette.EyeShine);
                c.HLine(x - 3, x + 3, y, main);
                c.VLine(x, y - 3, y + 3, main);
                return;
            case 2: Rays(c, x, y, 3, 9, main); c.Rect(x - 1, y - 1, 3, 3, Palette.EyeShine); return;
            case 3: Rays(c, x, y, 8, 13, second); Rays(c, x, y, 3, 6, main); return;
            case 4: Dots(c, x, y, 14, second, 0); Dots(c, x, y, 9, main, 0); return;
            case 5: Dots(c, x, y, 15, main, 2); return;
        }
    }

    static void Rays(PixelCanvas c, int x, int y, int r0, int r1, byte col)
    {
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            c.Line(x + (int)Math.Round(Math.Cos(a) * r0), y + (int)Math.Round(Math.Sin(a) * r0),
                   x + (int)Math.Round(Math.Cos(a) * r1), y + (int)Math.Round(Math.Sin(a) * r1), col);
        }
    }

    static void Dots(PixelCanvas c, int x, int y, int r, byte col, int fall)
    {
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            int px = x + (int)Math.Round(Math.Cos(a) * r), py = y + (int)Math.Round(Math.Sin(a) * r) + fall;
            c.Rect(px, py, 2, 2, col);
        }
    }

    // ---- erreur ---------------------------------------------------------------

    /// <summary>Goutte de sueur : pointe en haut, reflet blanc.</summary>
    public static void SweatDrop(PixelCanvas c, int x, int y)
    {
        c.Ellipse(x, y + 3, 3, 3, Palette.AccentBlue);
        c.HLine(x - 1, x + 1, y, Palette.AccentBlue);
        c.Set(x, y - 1, Palette.AccentBlue);
        c.Set(x - 1, y + 2, Palette.EyeShine);
        c.Set(x - 1, y + 3, Palette.EyeShine);
    }

    // ---- FX des minis ---------------------------------------------------------

    /// <summary>Nuage de fumee : boules claires ombrees en bas a droite, reflet en haut a gauche.</summary>
    public static void Puff(PixelCanvas c, int x, int y, int r)
    {
        if (r <= 0) return;
        c.Ellipse(x, y, r, r, Palette.MuzzleShade);
        c.Ellipse(x - r / 5, y - r / 5, r * 4 / 5, r * 4 / 5, Palette.Muzzle);
        if (r >= 5) c.Ellipse(x - r / 3, y - r / 3, Math.Max(1, r / 4), Math.Max(1, r / 4), Palette.EyeShine);
    }

    /// <summary>Etincelle a quatre branches. size 0 = un point, 3 = grande croix.</summary>
    public static void Sparkle(PixelCanvas c, int x, int y, int size, byte col)
    {
        if (size < 0) return;
        c.Set(x, y, Palette.EyeShine);
        if (size == 0) return;
        c.HLine(x - size - 1, x - 1, y, col);
        c.HLine(x + 1, x + size + 1, y, col);
        c.VLine(x, y - size - 1, y - 1, col);
        c.VLine(x, y + 1, y + size + 1, col);
        if (size >= 2)
        {
            c.Set(x - 1, y - 1, Palette.EyeShine);
            c.Set(x + 1, y - 1, col);
            c.Set(x - 1, y + 1, col);
            c.Set(x + 1, y + 1, col);
        }
    }
}
