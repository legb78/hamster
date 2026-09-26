namespace Hamster.App.Render;

/// <summary>
/// Lit un clip, et sait revenir a un clip de fond quand un one-shot se termine.
/// La cadence du timer se cale sur le clip le plus rapide affiche, pas sur une
/// frequence fixe : un clip a 5 fps ne doit pas reveiller le CPU 12 fois par seconde.
/// </summary>
internal sealed class Animator
{
    readonly SpriteLibrary _library;
    double _elapsed;

    public RenderClip Current { get; private set; }
    public RenderClip Background { get; private set; }
    public int FrameIndex { get; private set; }
    public bool Finished { get; private set; }

    public Animator(SpriteLibrary library, string startClip)
    {
        _library = library;
        Current = Background = library[startClip];
    }

    /// <summary>Clip de fond : joue en boucle des qu'aucun one-shot n'est en cours.</summary>
    public void SetBackground(string name)
    {
        var clip = _library[name];
        if (Background == clip) return;
        Background = clip;
        if (Current.Loop) Play(name);
    }

    public void Play(string name)
    {
        var clip = _library[name];
        if (Current == clip && !Finished) return;
        Current = clip;
        FrameIndex = 0;
        _elapsed = 0;
        Finished = false;
    }

    public void Advance(double seconds)
    {
        if (Finished) return;
        _elapsed += seconds;
        double step = 1.0 / Current.Fps;
        while (_elapsed >= step)
        {
            _elapsed -= step;
            FrameIndex++;
            if (FrameIndex < Current.FrameCount) continue;

            if (Current.Loop) { FrameIndex = 0; continue; }

            FrameIndex = Current.FrameCount - 1;
            Finished = true;
            Current = Background;
            FrameIndex = 0;
            Finished = false;
            break;
        }
    }

    public uint[] Frame(bool facingLeft) => Current.Frame(FrameIndex, facingLeft);
}
