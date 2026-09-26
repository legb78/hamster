namespace Hamster.Art;

/// <summary>
/// Etat d'une frame du personnage. Tout ce qui bouge passe par ici : le dessin
/// lui-meme est deterministe, ce qui garantit pivot et ligne de base identiques.
/// </summary>
public struct Pose
{
    /// <summary>Deplacement vertical du blob entier (respiration, saut). Negatif = vers le haut.</summary>
    public int BodyDy;
    /// <summary>Ecrasement : positif = aplati, negatif = etire.</summary>
    public int Squash;
    /// <summary>Decalage horizontal du blob, en px.</summary>
    public int HeadDx;
    /// <summary>0 = pattes au repos, 1 et 2 = pas alternes.</summary>
    public int LegPhase;
    public bool Blink;
    /// <summary>Index dans Palette.BowHues.</summary>
    public int BowHue;
    public bool Shadow;
    public bool Heart;
    public int HeartDy;

    public static Pose Default => new() { Shadow = true, BowHue = 0 };
}

/// <summary>
/// Le personnage, dessine entierement en code.
///
/// Silhouette en patate, sans tete ni corps distincts : c'est ce qui fait lire le
/// meme. Deux signatures tenues dans 100 % des frames : les yeux noirs enormes avec
/// leur reflet en haut a gauche, et l'unique noeud rose pose sur le cote du crane.
/// Le reste peut varier.
/// </summary>
public static class HamsterSprite
{
    const int CenterX = 64;
    const int CenterY = 72;

    public static byte[] Render(in Pose pose)
    {
        var c = new PixelCanvas();
        Draw(c, pose);
        return c.Snapshot();
    }

    public static void Draw(PixelCanvas c, in Pose pose)
    {
        int dy = pose.BodyDy;
        int sq = pose.Squash;
        int cx = CenterX + pose.HeadDx;
        int cy = CenterY + dy + sq;
        var (bowDark, bowMid, bowLight) = Palette.BowHues[pose.BowHue % Palette.BowHues.Length];

        // ---- le blob ------------------------------------------------------
        DrawBlob(c, cx, cy, sq);

        // ---- oreille : posee APRES le blob et debordant a peine de la silhouette.
        // Avant le blob elle etait avalee ; trop grosse, elle lit comme un champignon.
        c.Ellipse(cx - 38, cy - 28, 8, 7, Palette.FurDark);

        // ---- museau --------------------------------------------------------
        int muzzleY = cy + 26;
        c.Ellipse(cx + 1, muzzleY, 21, 13, Palette.MuzzleShade);
        c.Ellipse(cx + 1, muzzleY - 1, 19, 11, Palette.Muzzle);

        // ---- nez et bouche tombante ----------------------------------------
        int noseY = muzzleY - 6;
        c.Ellipse(cx + 1, noseY, 4, 3, Palette.Nose);
        c.HLine(cx - 1, cx + 3, noseY + 2, Palette.NoseDark);
        c.VLine(cx + 1, noseY + 2, noseY + 5, Palette.NoseDark);
        // les commissures descendent : c'est la tout le personnage
        for (int i = 0; i < 4; i++)
        {
            c.Set(cx - 1 - i, noseY + 5 + (i + 1) / 2, Palette.NoseDark);
            c.Set(cx + 3 + i, noseY + 5 + (i + 1) / 2, Palette.NoseDark);
        }

        // ---- pattes, a peine visibles sous la masse -------------------------
        int pawY = 115 + Math.Max(0, dy);
        c.Ellipse(cx - 17, pawY + (pose.LegPhase == 1 ? -2 : 0), 10, 5, Palette.FurLight);
        c.Ellipse(cx + 17, pawY + (pose.LegPhase == 2 ? -2 : 0), 10, 5, Palette.FurLight);

        // ---- yeux : signature numero un ------------------------------------
        // legerement asymetriques, comme sur la reference
        DrawEye(c, cx - 19, cy + 3, 14, 15, pose.Blink);
        DrawEye(c, cx + 19, cy + 5, 13, 14, pose.Blink);

        // ---- noeud unique : signature numero deux ---------------------------
        DrawBow(c, cx + 25, cy - 29, bowDark, bowMid, bowLight);

        // ---- contour, puis ce qui ne doit pas etre contoure ------------------
        c.OutlineSilhouette(Palette.Outline);

        if (pose.Shadow)
        {
            int width = 32 - Math.Max(0, -dy);
            c.Ellipse(CenterX, PixelCanvas.Baseline + 2, Math.Max(12, width), 4, Palette.Shadow);
        }

        if (pose.Heart) DrawHeart(c, CenterX + 34, 26 + pose.HeartDy, bowMid, bowLight);
    }

    /// <summary>
    /// La patate. Plusieurs ellipses qui debordent les unes des autres : une seule
    /// ellipse donnerait un oeuf trop regulier, et la silhouette cesserait d'etre
    /// reconnaissable.
    /// </summary>
    static void DrawBlob(PixelCanvas c, int cx, int cy, int sq)
    {
        const byte f = Palette.FurMid;
        c.Ellipse(cx,      cy,      41, 37 - sq, f);
        c.Ellipse(cx - 20, cy - 20, 24, 20, f);
        c.Ellipse(cx + 22, cy - 16, 23, 19, f);
        c.Ellipse(cx +  8, cy - 30, 22, 14, f);
        c.Ellipse(cx -  2, cy + 20, 37, 22, f);
        c.Ellipse(cx - 33, cy +  6, 16, 17, f);
        c.Ellipse(cx + 34, cy +  8, 15, 15, f);

        // calotte sombre en haut, flancs plus clairs en bas. Les bords sont ceux
        // des ellipses elles-memes, pas une decoupe horizontale : c'est ce qui
        // donne du volume a une forme aussi plate.
        c.EllipseMasked(cx + 2, cy - 40, 42, 34, Palette.FurDark, f);
        c.EllipseMasked(cx - 2, cy + 48, 42, 34, Palette.FurLight, f);
        c.EllipseMasked(cx - 2, cy + 62, 36, 30, Palette.FurHi, Palette.FurLight);
    }

    static void DrawEye(PixelCanvas c, int cx, int cy, int rx, int ry, bool blink)
    {
        if (blink)
        {
            c.Ellipse(cx, cy, rx, 3, Palette.FurLight);
            c.HLine(cx - rx + 2, cx + rx - 2, cy, Palette.Outline);
            c.HLine(cx - rx + 4, cx + rx - 4, cy + 1, Palette.Outline);
            return;
        }

        c.Ellipse(cx, cy, rx, ry, Palette.EyeBlack);
        c.Ellipse(cx - rx / 2 - 1, cy - ry / 2 - 1, 5, 5, Palette.EyeShine);
        c.Ellipse(cx + rx / 2, cy + ry / 2, 2, 2, Palette.EyeShine2);
    }

    /// <summary>Noeud papillon : deux boucles evasees, un noeud central, deux pans qui retombent.</summary>
    static void DrawBow(PixelCanvas c, int cx, int cy, byte dark, byte mid, byte light)
    {
        const int len = 12;

        // les pans, dessines en premier pour passer derriere les boucles
        for (int i = 0; i < 8; i++)
        {
            c.VLine(cx - 3 - i, cy + 4 + i, cy + 9 + i, mid);
            c.VLine(cx + 3 + i, cy + 4 + i, cy + 9 + i, mid);
        }
        c.VLine(cx - 10, cy + 11, cy + 16, dark);
        c.VLine(cx + 10, cy + 11, cy + 16, dark);

        // les deux boucles
        for (int i = 0; i <= len; i++)
        {
            int h = 2 + i * 6 / len;
            c.VLine(cx - 4 - i, cy - h, cy + h, mid);
            c.VLine(cx + 4 + i, cy - h, cy + h, mid);
        }
        c.VLine(cx - 4 - len, cy - 8, cy + 8, dark);
        c.VLine(cx + 4 + len, cy - 8, cy + 8, dark);
        c.HLine(cx - 15, cx - 6, cy - 5, light);
        c.HLine(cx + 6, cx + 15, cy - 5, light);
        c.HLine(cx - 15, cx - 5, cy + 6, dark);
        c.HLine(cx + 5, cx + 15, cy + 6, dark);

        // le noeud
        c.Ellipse(cx, cy, 4, 5, dark);
        c.Ellipse(cx - 1, cy - 1, 2, 3, mid);
        c.Ellipse(cx - 1, cy - 2, 1, 1, light);
    }

    static void DrawHeart(PixelCanvas c, int cx, int cy, byte mid, byte light)
    {
        c.Ellipse(cx - 3, cy - 2, 4, 4, mid);
        c.Ellipse(cx + 3, cy - 2, 4, 4, mid);
        for (int i = 0; i <= 7; i++) c.HLine(cx - 7 + i, cx + 7 - i, cy + i, mid);
        c.Ellipse(cx - 3, cy - 3, 2, 2, light);
    }
}
