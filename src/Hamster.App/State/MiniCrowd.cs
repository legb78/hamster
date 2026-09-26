using Hamster.Activity;
using Hamster.App.Render;
using Hamster.Art;

namespace Hamster.App.State;

/// <summary>Un mini hamster a l'ecran : sous-agent ou autre session active.</summary>
internal sealed class Mini
{
    public required string Id { get; init; }
    public required string Kind { get; set; }
    public required string Label { get; set; }
    public required PetState State { get; set; }
    public required int Hue { get; init; }
    /// <summary>Scene de travail stable, derivee de l'Id : un sous-agent garde la sienne du debut a la fin.</summary>
    public required string Scene { get; init; }
    public required Animator Body { get; init; }
    /// <summary>Fumee d'apparition ou etincelles de disparition, null le reste du temps.</summary>
    public Animator? Fx { get; set; }
    /// <summary>Decalage d'angle sur l'orbite, en radians.</summary>
    public double Phase { get; set; }
    /// <summary>Instant de disparition : le mini n'est plus qu'etincelles.</summary>
    public double? Gone { get; set; }
    /// <summary>Dephasage du petit rebond, pour que les minis ne sautillent pas en choeur.</summary>
    public required double BobSeed { get; init; }

    /// <summary>
    /// Le personnage est cache pendant ses deux premieres frames de fumee (le nuage n'est pas
    /// encore assez gros pour le couvrir), et des qu'il disparait.
    /// </summary>
    public bool ShowsBody => Gone == null && (Fx == null || Fx.Current.Name != Clips.FxPop || Fx.FrameIndex >= 2);
    public bool Dead => Gone != null && (Fx == null || Fx.Finished);
}

/// <summary>
/// Les minis et leur orbite. Pur calcul, sans fenetre : le temps arrive en secondes, le
/// dessin est fait par le controleur. L'orbite est une ellipse autour des pieds du
/// principal ; ici on ne tient que les angles, la geometrie est dans Orbit.
/// </summary>
internal sealed class MiniCrowd
{
    /// <summary>Un tour complet d'orbite.</summary>
    public const double TurnSeconds = 40;
    /// <summary>Vitesse maximale a laquelle un mini glisse vers sa place quand la foule change.</summary>
    const double GlideRadPerSecond = 0.9;
    /// <summary>Angle ou nait un mini : de face, a droite du principal, la ou on le voit apparaitre.</summary>
    const double BirthAngle = 20 * Math.PI / 180;

    readonly SpriteLibrary _library;
    readonly List<Mini> _minis = new();

    public MiniCrowd(SpriteLibrary library) => _library = library;

    public IReadOnlyList<Mini> Minis => _minis;
    public int Overflow { get; private set; }
    /// <summary>Au moins un mini a l'ecran, etincelles comprises.</summary>
    public bool Any => _minis.Count > 0;

    /// <summary>Angle courant sur l'orbite. Il decroit : de face, les minis vont vers la droite.</summary>
    public static double AngleAt(double phase, double now) => phase - 2 * Math.PI * now / TurnSeconds;
    public double AngleOf(Mini m, double now) => AngleAt(m.Phase, now);

    /// <summary>
    /// Aligne la foule sur le dernier instantane. Rend les minis qui viennent d'apparaitre.
    /// animate faux (hamster cache ou en pause) : ni fumee ni etincelles, qui se joueraient
    /// en differe au reveil.
    /// </summary>
    public IReadOnlyList<Mini> Sync(IReadOnlyList<MiniInfo> infos, int overflow, double now, bool animate = true)
    {
        Overflow = overflow;
        var born = new List<Mini>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in infos)
        {
            seen.Add(info.Id);
            var m = _minis.Find(x => x.Id == info.Id && x.Gone == null);
            if (m == null)
            {
                m = new Mini
                {
                    Id = info.Id,
                    Kind = info.Kind,
                    Label = info.Label,
                    State = info.State,
                    Hue = Palette.HueIndexFor(info.ColorKey),
                    Scene = SceneFor(info.Id),
                    Body = new Animator(_library, SceneFor(info.Id)),
                    BobSeed = Hash(info.Id) % 1000 / 1000.0 * Math.PI * 2,
                    // nait de face, a droite, puis glisse vers sa place
                    Phase = BirthAngle + 2 * Math.PI * now / TurnSeconds,
                };
                if (animate) m.Fx = new Animator(_library, Clips.FxPop);
                _minis.Add(m);
                born.Add(m);
            }
            m.Kind = info.Kind;
            m.Label = info.Label;
            m.State = info.State;
            m.Body.Show(ClipFor(m));
        }
        foreach (var m in _minis)
        {
            if (m.Gone != null || seen.Contains(m.Id)) continue;
            m.Gone = now;
            m.Fx = animate ? new Animator(_library, Clips.FxSparkle) : null;
        }
        if (!animate) _minis.RemoveAll(m => m.Dead);
        return born;
    }

    public void Advance(double seconds, double now)
    {
        foreach (var m in _minis)
        {
            m.Body.Advance(seconds);
            if (m.Fx != null)
            {
                m.Fx.Advance(seconds);
                if (m.Fx.Finished && m.Gone == null) m.Fx = null;
            }
        }
        _minis.RemoveAll(m => m.Dead);

        // places regulierement espacees, dans l'ordre d'arrivee, a partir du plus ancien qui ne
        // bouge pas : un mini seul ne fait donc aucun detour. Les autres y glissent sans a-coup
        var active = _minis.Where(m => m.Gone == null).ToList();
        double step = seconds * GlideRadPerSecond;
        for (int i = 1; i < active.Count; i++)
        {
            double target = active[0].Phase + 2 * Math.PI * i / active.Count;
            double delta = Wrap(target - active[i].Phase);
            active[i].Phase += Math.Clamp(delta, -step, step);
        }
    }

    public void React(string id)
    {
        var m = _minis.Find(x => x.Id == id && x.Gone == null);
        m?.Body.PlayOnce(Clips.React);
    }

    /// <summary>Clip du mini selon son etat. Jamais think : un mini ne fait que travailler.</summary>
    public static string ClipFor(Mini m) => m.State switch
    {
        PetState.WaitingUser => Clips.Phone,
        PetState.Error => Clips.Error,
        PetState.Celebrating => Clips.Celebrate,
        _ => m.Scene,
    };

    /// <summary>Scene de travail stable pour un id : tirage pondere par le hash, think exclu.</summary>
    public static string SceneFor(string id)
    {
        var pool = Clips.WorkPool.Where(p => p.Name != Clips.Think).ToArray();
        int total = pool.Sum(p => p.Weight);
        int r = (int)(Hash(id) % (uint)total);
        foreach (var (name, weight) in pool)
        {
            if (r < weight) return name;
            r -= weight;
        }
        return pool[0].Name;
    }

    /// <summary>FNV-1a : stable d'un lancement a l'autre, contrairement a string.GetHashCode.</summary>
    static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        if (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
