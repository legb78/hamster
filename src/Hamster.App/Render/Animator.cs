using Hamster.Art;

namespace Hamster.App.Render;

/// <summary>
/// Lit un clip. Le directeur dit quel clip il veut (Show) ; un one-shot en cours (reaction,
/// erreur, fete) va au bout avant de lui ceder la place, sauf si le clip voulu est plus
/// prioritaire : le telephone coupe une erreur, pas l'inverse. Un one-shot fini qui reste
/// voulu se fige sur sa derniere frame au lieu de boucler.
/// </summary>
internal sealed class Animator
{
    readonly SpriteLibrary _library;
    RenderClip _desired;
    double _elapsed;

    public RenderClip Current { get; private set; }
    public int FrameIndex { get; private set; }
    /// <summary>One-shot arrive au bout et fige sur sa derniere frame.</summary>
    public bool Finished { get; private set; }

    public Animator(SpriteLibrary library, string startClip)
    {
        _library = library;
        Current = _desired = library[startClip];
    }

    public RenderClip Desired => _desired;

    /// <summary>Clip voulu. Sans effet si c'est deja le clip voulu.</summary>
    public void Show(string name)
    {
        var clip = _library[name];
        if (clip == _desired) return;
        _desired = clip;
        if (Current == clip && !Finished) return;
        if (Current.Loop || Finished || Rank(clip.Name) > Rank(Current.Name)) Start(clip);
    }

    /// <summary>Joue un one-shot tout de suite (clic), puis revient au clip voulu.</summary>
    public void PlayOnce(string name) => Start(_library[name]);

    public void Advance(double seconds)
    {
        if (Finished) return;
        _elapsed += seconds;
        while (_elapsed >= 1.0 / Current.Fps)
        {
            _elapsed -= 1.0 / Current.Fps;
            FrameIndex++;
            if (FrameIndex < Current.FrameCount) continue;

            if (Current.Loop) { FrameIndex = 0; continue; }
            if (_desired != Current)
            {
                // Start remet le temps a zero : le nouveau clip part de sa premiere frame
                Start(_desired);
                break;
            }
            FrameIndex = Current.FrameCount - 1;
            Finished = true;
            break;
        }
    }

    void Start(RenderClip clip)
    {
        Current = clip;
        FrameIndex = 0;
        _elapsed = 0;
        Finished = false;
    }

    /// <summary>
    /// Priorite des clips qui ne bouclent pas, alignee sur celle des etats :
    /// fete > telephone > erreur > reaction au clic. Les boucles s'interrompent toujours.
    /// </summary>
    static int Rank(string name) => name switch
    {
        Clips.Celebrate => 4,
        Clips.Phone => 3,
        Clips.Error => 2,
        Clips.React => 1,
        _ => 0,
    };

    public byte[] Frame => Current.Frames[FrameIndex];
    public byte[] MiniFrame => Current.Mini(FrameIndex);
}
