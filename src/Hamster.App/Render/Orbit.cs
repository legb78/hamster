namespace Hamster.App.Render;

/// <summary>
/// Geometrie de l'orbite des minis, en pixels sprite du principal. Une ellipse centree sur
/// ses pieds : l'arc arriere (en haut) passe derriere lui, l'arc avant (en bas) devant.
///
/// Pour que l'arc avant ne croise jamais le visage, un mini de devant doit garder la tete
/// sous le museau, meme quand le principal s'ecrase a la reception d'un saut : il faut ses
/// pieds 44 px sous ceux du principal (mesure au pixel par Hamster.App.Tests, sur toutes les
/// frames). La fenetre ne descend pas sous la zone de travail : c'est donc le principal qui
/// monte de Lift pixels tant qu'il y a des minis. En perspective, il se tient au centre de
/// l'anneau, plus loin que l'arc avant. Filet de securite pour ce qui depasse du corps (bulle
/// du telephone, feux d'artifice) : un mini de devant ne peint jamais sur le visage
/// (Compositor.Face).
/// </summary>
internal static class Orbit
{
    /// <summary>Demi-axe horizontal : de cote, un mini ne chevauche pas le corps du principal.</summary>
    public const int Rx = 84;
    /// <summary>Demi-axe vertical : de face, la tete d'un mini reste sous le museau du principal.</summary>
    public const int Ry = 44;
    public const int Bob = 1;

    /// <summary>
    /// Montee du principal avec des minis : le mini le plus bas (Ry + Bob sous les pieds, plus
    /// ses 3 lignes sous les pieds) doit tenir au-dessus du bas de la fenetre, ou le principal
    /// n'a que 6 lignes sous les pieds.
    /// </summary>
    public const int Lift = Ry + Bob + (SpriteLibrary.MiniSize - SpriteLibrary.MiniBaseline - 1)
                            - (SpriteLibrary.Size - SpriteLibrary.Baseline - 1);

    /// <summary>Demi-largeur occupee par l'orbite, minis compris.</summary>
    public const int HalfWidth = Rx + SpriteLibrary.MiniSize / 2;

    /// <summary>Badge "+N" : a droite de l'anneau, au-dessus des minis qui passent de ce cote.</summary>
    public const int BadgeX = Rx + 20;
    public const int BadgeAboveFeet = 56;

    /// <summary>Petit rebond vertical, en pixels, d'une periode de 1,2 s.</summary>
    public static int BobAt(double now, double seed) =>
        (int)Math.Round(Bob * Math.Sin(2 * Math.PI * now / 1.2 + seed));

    /// <summary>Position des pieds d'un mini par rapport a ceux du principal. Y positif = devant.</summary>
    public static (int Dx, int Dy) At(double angle) =>
        ((int)Math.Round(Rx * Math.Cos(angle)), (int)Math.Round(Ry * Math.Sin(angle)));
}
