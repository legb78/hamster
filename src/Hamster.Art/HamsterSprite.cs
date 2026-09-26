namespace Hamster.Art;

/// <summary>Formes de bouche. Droop, la bouche tombante, est le personnage : les autres restent des exceptions.</summary>
public enum Mouth { Droop, Open, Talk, Wail, Chew, Yawn }

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
    /// <summary>Decalage horizontal du blob, en px. L'ombre ne suit pas : c'est un penche, pas un pas.</summary>
    public int HeadDx;
    /// <summary>0 = pattes au repos, 1 et 2 = pas alternes.</summary>
    public int LegPhase;
    public bool Blink;
    /// <summary>Index dans Palette.BowHues.</summary>
    public int BowHue;
    public bool Shadow;
    public bool Heart;
    public int HeartDy;

    /// <summary>Decalage du personnage entier, ombre comprise : laisse la place a un objet pose au sol.</summary>
    public int OffsetX;
    /// <summary>Regard : les yeux glissent dans la face vers ce que le personnage regarde.</summary>
    public int EyeDx, EyeDy;
    /// <summary>Paupiere, en pourcentage de l'oeil couvert. Au-dela de 70 l'oeil noir ne se lit plus.</summary>
    public int Lid;
    public Mouth Mouth;
    /// <summary>Phase de mastication, pour Mouth.Chew.</summary>
    public int ChewPhase;
    /// <summary>Bajoues : 0 = vides, 5 = pleines.</summary>
    public int Cheeks;
    public bool Glasses;
    /// <summary>Reflet des verres : couleur de l'ecran regarde, 0 = reflet neutre.</summary>
    public byte GlassesGlint;
    public bool Tears;
    /// <summary>Blouse de labo : le bas du corps devient blanc, revers en V.</summary>
    public bool Coat;
    /// <summary>En l'air : les pattes suivent le corps au lieu de rester au sol.</summary>
    public bool Airborne;
    /// <summary>Masque le noeud. Sert UNIQUEMENT au controle des indices 13 a 15 de SpriteGen.</summary>
    public bool HideBow;

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
    public const int CenterX = 64;
    public const int CenterY = 72;

    public static byte[] Render(in Pose pose)
    {
        var c = new PixelCanvas();
        Draw(c, pose);
        return c.Snapshot();
    }

    /// <summary>Le personnage complet : corps, contour, ombre et coeur.</summary>
    public static void Draw(PixelCanvas c, in Pose pose)
    {
        DrawBody(c, pose);
        c.OutlineSilhouette(Palette.Outline);
        if (pose.Shadow) DrawShadow(c, pose);
        if (pose.Heart) DrawHeart(c, CenterX + pose.OffsetX + 34, 26 + pose.HeartDy);
    }

    /// <summary>
    /// Ombre au sol, posee SOUS ce qui est deja dessine. Posee par-dessus, elle mangeait
    /// le bas des pattes et la ligne de base n'etait plus a 121.
    /// </summary>
    public static void DrawShadow(PixelCanvas c, in Pose pose)
    {
        int width = 32 - Math.Max(0, -pose.BodyDy);
        c.EllipseUnder(CenterX + pose.OffsetX, PixelCanvas.Baseline + 2, Math.Max(12, width), 4, Palette.Shadow);
    }

    /// <summary>Le corps seul, sans contour : les scenes le posent sur leur propre couche.</summary>
    public static void DrawBody(PixelCanvas c, in Pose pose)
    {
        int dy = pose.BodyDy;
        int sq = pose.Squash;
        int cx = CenterX + pose.OffsetX + pose.HeadDx;
        int cy = CenterY + dy + sq;
        var (bowDark, bowMid, bowLight) = Palette.BowHues[pose.BowHue % Palette.BowHues.Length];

        // ---- le blob ------------------------------------------------------
        DrawBlob(c, cx, cy, sq);
        if (pose.Cheeks > 0) DrawCheeks(c, cx, cy, pose.Cheeks);
        if (pose.Coat) DrawCoat(c, cx, cy);

        // ---- oreille : posee APRES le blob et debordant a peine de la silhouette.
        // Avant le blob elle etait avalee ; trop grosse, elle lit comme un champignon.
        c.Ellipse(cx - 38, cy - 28, 8, 7, Palette.FurDark);

        // ---- museau --------------------------------------------------------
        int muzzleY = cy + 26;
        c.Ellipse(cx + 1, muzzleY, 21, 13, Palette.MuzzleShade);
        c.Ellipse(cx + 1, muzzleY - 1, 19, 11, Palette.Muzzle);

        DrawMouth(c, cx, muzzleY - 6, pose);

        // ---- pattes, a peine visibles sous la masse -------------------------
        int pawY = 115 + (pose.Airborne ? dy : Math.Max(0, dy));
        c.Ellipse(cx - 17, pawY + (pose.LegPhase == 1 ? -2 : 0), 10, 5, Palette.FurLight);
        c.Ellipse(cx + 17, pawY + (pose.LegPhase == 2 ? -2 : 0), 10, 5, Palette.FurLight);

        // ---- yeux : signature numero un ------------------------------------
        // legerement asymetriques, comme sur la reference. La fourrure d'avant les
        // yeux sert de paupiere : elle a deja le bon degrade.
        var fur = pose.Lid > 0 ? c.Snapshot() : null;
        int lx = cx - 19 + pose.EyeDx, ly = cy + 3 + pose.EyeDy;
        int rx = cx + 19 + pose.EyeDx, ry = cy + 5 + pose.EyeDy;
        DrawEye(c, fur, lx, ly, 14, 15, pose, outerLeft: true);
        DrawEye(c, fur, rx, ry, 13, 14, pose, outerLeft: false);
        if (pose.Glasses) DrawGlasses(c, lx, ly, rx, ry, pose.GlassesGlint);

        // ---- noeud unique : signature numero deux ---------------------------
        if (!pose.HideBow) DrawBow(c, cx + 25, cy - 29, bowDark, bowMid, bowLight);
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

    /// <summary>
    /// Bajoues : elles elargissent la silhouette elle-meme. Une bajoue dessinee a
    /// l'interieur du blob ne se voit pas a 1x ; c'est le bord qui gonfle qui se lit.
    /// </summary>
    static void DrawCheeks(PixelCanvas c, int cx, int cy, int level)
    {
        level = Math.Clamp(level, 0, 5);
        int r = 6 + level * 2;
        foreach (int side in new[] { -1, 1 })
        {
            int px = cx + side * (30 + level * 2);
            int py = cy + 20;
            c.Ellipse(px, py, r, r - 1, Palette.FurLight);
            c.EllipseMasked(px - side * 2, py - r / 2, r - 2, r / 2, Palette.FurHi, Palette.FurLight);
            // un pli sombre au-dessus : c'est lui qui separe la bajoue de la tete
            for (int i = -r / 2; i <= r / 2; i++) c.Set(px + i, py - r + 1 + (i * i) / (r * 2), Palette.FurMid);
        }
    }

    /// <summary>
    /// Blouse : toute la fourrure sous la ligne des yeux passe au blanc, le museau et les
    /// pieds se posent ensuite par-dessus. Revers en V de part et d'autre du museau.
    /// </summary>
    static void DrawCoat(PixelCanvas c, int cx, int cy)
    {
        for (int y = cy + 16; y < PixelCanvas.Size; y++)
        for (int x = 0; x < PixelCanvas.Size; x++)
        {
            byte v = c.Get(x, y);
            if (v is not (Palette.FurMid or Palette.FurLight or Palette.FurHi or Palette.FurDark)) continue;
            // ombre de la blouse sur le flanc droit et en bas
            bool shade = x > cx + 30 || y > cy + 36;
            c.Set(x, y, shade ? Palette.EyeShine2 : Palette.EyeShine);
        }
        // col : une bande grise qui suit le haut de la blouse
        for (int x = cx - 50; x <= cx + 50; x++)
        {
            byte v = c.Get(x, cy + 16);
            if (v is Palette.EyeShine or Palette.EyeShine2) c.Set(x, cy + 16, Palette.MetalLight);
        }
        // revers en V qui plongent vers le museau
        c.Line(cx - 36, cy + 17, cx - 22, cy + 30, Palette.MetalLight);
        c.Line(cx + 37, cy + 17, cx + 23, cy + 30, Palette.MetalLight);
        // poche de poitrine, avec un stylo
        c.Rect(cx + 28, cy + 26, 7, 1, Palette.MetalLight);
        c.VLine(cx + 30, cy + 22, cy + 26, Palette.AccentBlue);
    }

    static void DrawMouth(PixelCanvas c, int cx, int noseY, in Pose pose)
    {
        // ---- nez et philtrum ------------------------------------------------
        c.Ellipse(cx + 1, noseY, 4, 3, Palette.Nose);
        c.HLine(cx - 1, cx + 3, noseY + 2, Palette.NoseDark);
        // philtrum : plus court quand la bouche s'ouvre juste dessous, sinon les deux font une barre
        c.VLine(cx + 1, noseY + 2, pose.Mouth == Mouth.Talk ? noseY + 3 : noseY + 5, Palette.NoseDark);

        switch (pose.Mouth)
        {
            case Mouth.Open:
                // grande bouche en D : bord haut droit, fond arrondi, langue. Une petite
                // ellipse sous le philtrum lisait comme une barre verticale a 1x.
                for (int dy = 0; dy <= 6; dy++)
                {
                    int half = Span(6, 6, dy);
                    c.HLine(cx + 1 - half, cx + 1 + half, noseY + 4 + dy, Palette.NoseDark);
                }
                for (int dy = 0; dy <= 3; dy++)
                {
                    int half = Span(4, 4, dy);
                    c.HLine(cx + 1 - half, cx + 1 + half, noseY + 5 + dy, Palette.EarInner);
                }
                c.HLine(cx - 1, cx + 3, noseY + 8, Palette.Nose);
                return;
            case Mouth.Talk:
                c.Ellipse(cx + 1, noseY + 7, 4, 2, Palette.NoseDark);
                c.HLine(cx - 1, cx + 3, noseY + 7, Palette.EarInner);
                return;
            case Mouth.Wail:
                // grande bouche carree aux coins tombants : la catastrophe
                c.Ellipse(cx + 1, noseY + 9, 7, 4, Palette.NoseDark);
                c.Ellipse(cx + 1, noseY + 10, 5, 2, Palette.EarInner);
                c.HLine(cx - 2, cx + 4, noseY + 12, Palette.Nose);
                for (int i = 0; i < 3; i++)
                {
                    c.Set(cx - 6 - i, noseY + 10 + i, Palette.NoseDark);
                    c.Set(cx + 8 + i, noseY + 10 + i, Palette.NoseDark);
                }
                return;
            case Mouth.Yawn:
                c.Ellipse(cx + 1, noseY + 10, 5, 6, Palette.NoseDark);
                c.Ellipse(cx + 1, noseY + 11, 3, 4, Palette.EarInner);
                c.HLine(cx - 1, cx + 3, noseY + 14, Palette.Nose);
                return;
            case Mouth.Chew:
                // les commissures remontent d'un pixel une frame sur deux
                int up = pose.ChewPhase % 2;
                for (int i = 0; i < 4; i++)
                {
                    c.Set(cx - 1 - i, noseY + 5 + (i + 1) / 2 - up * (i / 2), Palette.NoseDark);
                    c.Set(cx + 3 + i, noseY + 5 + (i + 1) / 2 - up * (i / 2), Palette.NoseDark);
                }
                return;
        }

        // les commissures descendent : c'est la tout le personnage
        for (int i = 0; i < 4; i++)
        {
            c.Set(cx - 1 - i, noseY + 5 + (i + 1) / 2, Palette.NoseDark);
            c.Set(cx + 3 + i, noseY + 5 + (i + 1) / 2, Palette.NoseDark);
        }
    }

    static void DrawEye(PixelCanvas c, byte[]? fur, int cx, int cy, int rx, int ry, in Pose pose, bool outerLeft)
    {
        if (pose.Blink)
        {
            c.Ellipse(cx, cy, rx, 3, Palette.FurLight);
            c.HLine(cx - rx + 2, cx + rx - 2, cy, Palette.Outline);
            c.HLine(cx - rx + 4, cx + rx - 4, cy + 1, Palette.Outline);
            return;
        }

        c.Ellipse(cx, cy, rx, ry, Palette.EyeBlack);
        c.Ellipse(cx - rx / 2 - 1, cy - ry / 2 - 1, 5, 5, Palette.EyeShine);
        c.Ellipse(cx + rx / 2, cy + ry / 2, 2, 2, Palette.EyeShine2);

        if (pose.Lid > 0 && fur != null)
        {
            // La paupiere descend jusqu'a un bord COURBE, plus bas au milieu : un bord
            // droit lisait comme des lunettes de soleil. Au-dessus, la fourrure revient,
            // reflet compris, meme la ou il deborde de l'oeil.
            int baseY = cy - ry + (2 * ry + 1) * Math.Clamp(pose.Lid, 0, 100) / 100;
            for (int x = cx - rx - 6; x <= cx + rx + 6; x++)
            {
                double u = (x - cx) / (double)rx;
                int edgeY = baseY + (int)Math.Round(3 * Math.Max(0, 1 - u * u));
                for (int y = cy - ry - 6; y <= cy + ry; y++)
                {
                    if ((uint)x >= PixelCanvas.Size || (uint)y >= PixelCanvas.Size) continue;
                    int i = y * PixelCanvas.Size + x;
                    byte v = c.Px[i];
                    bool eye = v is Palette.EyeBlack or Palette.EyeShine or Palette.EyeShine2;
                    if (!eye) continue;
                    if (y < edgeY) c.Px[i] = fur[i];
                    else if (y == edgeY || (y == edgeY + 1 && Math.Abs(u) < 0.5)) c.Px[i] = Palette.Outline;
                }
            }
            // le cil deborde d'un pixel cote exterieur : c'est ce qui fait lire "endormi"
            int edge = Span(rx, ry, baseY - cy);
            if (edge >= 0) c.Set(outerLeft ? cx - edge - 1 : cx + edge + 1, baseY + 1, Palette.Outline);
        }

        if (pose.Tears)
        {
            // larmes qui montent au bord de l'oeil, puis une goutte au coin exterieur
            int wy = cy + ry - 3;
            int span = Span(rx, ry, wy - cy);
            c.HLine(cx - span + 2, cx + span - 2, wy, Palette.AccentBlue);
            c.HLine(cx - span + 4, cx + span - 4, wy + 1, Palette.AccentBlue);
            int dx = outerLeft ? cx - rx + 3 : cx + rx - 3;
            c.Ellipse(dx, cy + ry + 3, 2, 3, Palette.AccentBlue);
            c.Set(dx, cy + ry, Palette.AccentBlue);
            c.Set(dx - 1, cy + ry + 2, Palette.EyeShine);
        }
    }

    static int Span(int rx, int ry, int dy)
    {
        double t = 1.0 - (double)(dy * dy) / (double)(ry * ry);
        return t < 0 ? -1 : (int)(rx * Math.Sqrt(t) + 0.5);
    }

    /// <summary>
    /// Lunettes rondes : monture claire cernee de noir, pont, branches, un reflet par verre.
    /// Une monture noire collee a l'oeil noir se lisait comme un oeil plus gros.
    /// </summary>
    static void DrawGlasses(PixelCanvas c, int lx, int ly, int rx, int ry, byte glint)
    {
        c.Ring(lx, ly, 19, 20, 1, Palette.Outline);
        c.Ring(rx, ry, 18, 19, 1, Palette.Outline);
        c.Ring(lx, ly, 18, 19, 2, Palette.MetalLight);
        c.Ring(rx, ry, 17, 18, 2, Palette.MetalLight);
        c.HLine(lx + 17, rx - 16, ly - 2, Palette.Outline);
        c.HLine(lx + 17, rx - 16, ly - 1, Palette.MetalLight);
        c.HLine(lx + 17, rx - 16, ly, Palette.Outline);
        c.HLine(lx - 25, lx - 19, ly - 5, Palette.Outline);
        c.HLine(lx - 25, lx - 19, ly - 4, Palette.MetalLight);
        c.HLine(rx + 18, rx + 24, ry - 5, Palette.Outline);
        c.HLine(rx + 18, rx + 24, ry - 4, Palette.MetalLight);

        byte g = glint == 0 ? Palette.EyeShine2 : glint;
        c.Line(lx + 5, ly - 7, lx + 9, ly - 11, g);
        c.Line(lx + 7, ly - 6, lx + 10, ly - 9, g);
        c.Line(rx + 4, ry - 7, rx + 8, ry - 11, g);
        c.Line(rx + 6, ry - 6, rx + 9, ry - 9, g);
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

    /// <summary>
    /// Coeur rouge, plus rose : les indices 13 a 15 sont reserves au noeud, que les
    /// minis recolorent par substitution. Un coeur rose changerait de couleur avec eux.
    /// </summary>
    static void DrawHeart(PixelCanvas c, int cx, int cy)
    {
        c.Ellipse(cx - 3, cy - 2, 4, 4, Palette.AccentRed);
        c.Ellipse(cx + 3, cy - 2, 4, 4, Palette.AccentRed);
        for (int i = 0; i <= 7; i++) c.HLine(cx - 7 + i, cx + 7 - i, cy + i, Palette.AccentRed);
        c.Ellipse(cx - 3, cy - 3, 2, 2, Palette.EyeShine);
    }

    /// <summary>
    /// Une main : les pattes avant n'existent pas dans la silhouette, elles se posent
    /// sur leur propre couche quand un clip en a besoin (clavier, telephone, loupe).
    /// Plus claire que le corps : plus sombre, elle se lisait comme un caillou a 1x.
    /// </summary>
    public static void DrawPaw(PixelCanvas c, int x, int y, bool far = false)
    {
        c.Ellipse(x, y, 6, 5, far ? Palette.FurLight : Palette.FurHi);
        c.Ellipse(x - 2, y - 2, 2, 1, far ? Palette.FurHi : Palette.Muzzle);
        c.VLine(x - 2, y + 2, y + 4, far ? Palette.FurMid : Palette.FurLight);
        c.VLine(x + 2, y + 2, y + 4, far ? Palette.FurMid : Palette.FurLight);
    }
}
