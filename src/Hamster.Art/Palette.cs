namespace Hamster.Art;

/// <summary>
/// Palette indexee fixe, versionnee. Toute couleur dessinee DOIT venir d'ici :
/// c'est ce que Tools/spritecheck.py verifie, et c'est ce qui garantit qu'une sheet
/// regeneree plus tard par un modele d'image reste coherente avec le procedural.
/// </summary>
public static class Palette
{
    public const byte Transparent = 0;
    public const byte Outline     = 1;
    public const byte FurDark     = 2;
    public const byte FurMid      = 3;
    public const byte FurLight    = 4;
    public const byte FurHi       = 5;
    public const byte MuzzleShade = 6;
    public const byte Muzzle      = 7;
    public const byte EyeBlack    = 8;
    public const byte EyeShine    = 9;
    public const byte EyeShine2   = 10;
    public const byte NoseDark    = 11;
    public const byte Nose        = 12;
    public const byte BowDark     = 13;
    public const byte BowMid      = 14;
    public const byte BowLight    = 15;
    public const byte EarInner    = 16;
    public const byte Shadow      = 17;
    public const byte MetalDark   = 18;
    public const byte Metal       = 19;
    public const byte MetalLight  = 20;
    public const byte ScreenDark  = 21;
    public const byte ScreenGreen = 22;
    public const byte Paper       = 23;
    public const byte PaperShade  = 24;
    public const byte WoodDark    = 25;
    public const byte Wood        = 26;
    public const byte AccentRed   = 27;
    public const byte AccentBlue  = 28;
    public const byte AccentYell  = 29;
    public const byte AccentGreen = 30;
    public const byte AccentPurp  = 31;

    public const int Count = 32;

    /// <summary>ARGB non premultiplie, 0xAARRGGBB. L'ordre est le contrat du .gpl.</summary>
    public static readonly uint[] Argb =
    {
        0x00000000, // 0  transparent
        0xFF1C1A1A, // 1  outline
        0xFF4A4746, // 2  fur dark      -- la calotte du crane
        0xFF6B6766, // 3  fur mid       -- le gris dominant
        0xFF8A8684, // 4  fur light     -- flancs et bas du blob
        0xFFA8A3A0, // 5  fur highlight -- liseret du bas
        0xFFBFB9B5, // 6  museau ombre
        0xFFD8D2CE, // 7  museau clair
        0xFF141314, // 8  eye black
        0xFFFFFFFF, // 9  eye shine
        0xFFBFC6D2, // 10 eye shine 2
        0xFF8A6A6A, // 11 nose dark
        0xFFA98787, // 12 nose
        0xFFE2579C, // 13 bow dark
        0xFFFC8BC1, // 14 bow mid
        0xFFFFB8D8, // 15 bow light
        0xFF7A5F62, // 16 ear inner
        0x4D000000, // 17 shadow -- seule entree semi-transparente de la palette
        0xFF3A4150, // 18 metal dark
        0xFF5C6678, // 19 metal
        0xFF8A94A6, // 20 metal light
        0xFF0C1410, // 21 screen dark
        0xFF4CE07A, // 22 screen green
        0xFFF5E9C8, // 23 paper
        0xFFD9C79A, // 24 paper shade
        0xFF6B4A2F, // 25 wood dark
        0xFF9A6B42, // 26 wood
        0xFFE5484D, // 27 accent red
        0xFF4C8DF6, // 28 accent blue
        0xFFF5C542, // 29 accent yellow
        0xFF3FBF6A, // 30 accent green
        0xFFA06BE0, // 31 accent purple
    };

    public static readonly string[] Names =
    {
        "transparent", "outline", "fur dark", "fur mid", "fur light", "fur hi",
        "muzzle shade", "muzzle", "eye black", "eye shine", "eye shine 2",
        "nose dark", "nose", "bow dark", "bow mid", "bow light", "ear inner", "shadow",
        "metal dark", "metal", "metal light", "screen dark", "screen green",
        "paper", "paper shade", "wood dark", "wood",
        "accent red", "accent blue", "accent yellow", "accent green", "accent purple",
    };

    /// <summary>Six teintes de noeud pour les minis. L'index 0 est le rose du personnage principal.</summary>
    public static readonly (byte Dark, byte Mid, byte Light)[] BowHues =
    {
        (BowDark,     BowMid,      BowLight),
        (AccentRed,   AccentRed,   BowLight),
        (AccentBlue,  AccentBlue,  MetalLight),
        (AccentYell,  AccentYell,  Paper),
        (AccentGreen, AccentGreen, FurHi),
        (AccentPurp,  AccentPurp,  BowLight),
    };

    /// <summary>Hash FNV-1a stable : le meme workspace garde la meme couleur d'une session a l'autre.</summary>
    public static int HueIndexFor(string identity)
    {
        uint h = 2166136261;
        foreach (char c in identity) { h ^= c; h *= 16777619; }
        return (int)(h % (uint)BowHues.Length);
    }

    /// <summary>ARGB premultiplie : c'est ce qu'attend UpdateLayeredWindow.</summary>
    public static uint[] Premultiplied()
    {
        var o = new uint[Argb.Length];
        for (int i = 0; i < Argb.Length; i++)
        {
            uint c = Argb[i];
            uint a = c >> 24;
            if (a == 0) { o[i] = 0; continue; }
            if (a == 255) { o[i] = c; continue; }
            uint r = ((c >> 16) & 0xFF) * a / 255;
            uint g = ((c >> 8) & 0xFF) * a / 255;
            uint b = (c & 0xFF) * a / 255;
            o[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
        return o;
    }
}
