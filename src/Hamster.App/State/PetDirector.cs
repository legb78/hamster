using Hamster.Activity;
using Hamster.Art;

namespace Hamster.App.State;

/// <summary>Ce que le directeur attend de la balade.</summary>
internal enum Movement
{
    /// <summary>Immobile : travail, attente, humeurs, pose de repos.</summary>
    Hold,
    /// <summary>Balade libre, celle d'avant la phase 2 : le personnage decide seul.</summary>
    Roam,
    /// <summary>Marche imposee : petite balade du mode chill, ou skate.</summary>
    Walk,
}

/// <summary>
/// Traduit l'etat de Claude Code en clip et en deplacement pour le personnage principal.
/// Aucune horloge propre : le temps arrive en secondes par Update, ce qui le rend testable.
/// </summary>
internal sealed class PetDirector
{
    readonly Random _rng;
    readonly Func<string, bool> _hasClip;

    PetState _state = PetState.Idle;
    bool _started;

    // travail
    string? _workScene;
    double _workUntil;
    /// <summary>Scene tiree au hasard faute d'outil connu : le premier outil qui arrive la remplace.</summary>
    bool _workProvisional;

    // repos
    double _idleSince;
    ChillPhase _chill = ChillPhase.None;
    double _phaseUntil;
    string? _variant, _lastVariant;

    enum ChillPhase { None, Variant, Neutral, Stroll }

    public PetDirector(Func<string, bool> hasClip, Random? rng = null)
    {
        _hasClip = hasClip;
        _rng = rng ?? new Random();
    }

    public string Clip { get; private set; } = Clips.Idle;
    public Movement Movement { get; private set; } = Movement.Roam;
    /// <summary>Facteur de vitesse de la marche imposee : le skate roule plus vite qu'on ne marche.</summary>
    public double WalkSpeed { get; private set; } = 1.0;
    public PetState State => _state;
    /// <summary>Nom lisible de la phase, pour l'overlay de debug.</summary>
    public string Phase => _state == PetState.Idle ? (_chill == ChillPhase.None ? "balade" : "chill " + _chill) : _state.ToString();

    public void Update(PetState state, string? tool, double now, double chillDelaySeconds)
    {
        if (!_started || state != _state)
        {
            var previous = _started ? _state : PetState.Idle;
            _started = true;
            _state = state;
            Enter(state, previous, tool, now);
        }

        switch (_state)
        {
            case PetState.Working: UpdateWork(tool, now); break;
            case PetState.Idle: UpdateIdle(now, chillDelaySeconds); break;
        }
    }

    void Enter(PetState state, PetState previous, string? tool, double now)
    {
        WalkSpeed = 1.0;
        switch (state)
        {
            case PetState.Working:
                Movement = Movement.Hold;
                // apres une erreur on reprend la scene en cours : l'erreur n'est qu'une parenthese
                if (previous == PetState.Error && _workScene != null && now < _workUntil) Clip = _workScene;
                else BeginWork(tool, now);
                break;
            case PetState.Error:
                Movement = Movement.Hold;
                Clip = Clips.Error;
                break;
            case PetState.WaitingUser:
                Movement = Movement.Hold;
                Clip = Clips.Phone;
                _workScene = null;
                break;
            case PetState.Celebrating:
                Movement = Movement.Hold;
                Clip = Clips.Celebrate;
                _workScene = null;
                break;
            default:
                _workScene = null;
                _idleSince = now;
                _chill = ChillPhase.None;
                Movement = Movement.Roam;
                Clip = Clips.Idle;
                break;
        }
    }

    // ---- travail -------------------------------------------------------------

    /// <summary>La scene qui colle a l'outil, null si l'outil n'en appelle aucune.</summary>
    public string? SceneForTool(string? tool) => tool switch
    {
        "Bash" or "PowerShell" => Clips.WorkTerminal,
        "Read" or "Grep" or "Glob" or "WebFetch" or "WebSearch" => Clips.WorkBook,
        "Edit" or "Write" or "NotebookEdit" => Clips.WorkLaptop,
        // "Task" : l'ancien nom de l'outil Agent
        "Agent" or "Workflow" or "Task" => _hasClip(Clips.WorkConductor) ? Clips.WorkConductor : Clips.WorkLaptop,
        _ => null,
    };

    void BeginWork(string? tool, double now)
    {
        var byTool = SceneForTool(tool);
        _workProvisional = byTool == null;
        SetWorkScene(byTool ?? Pick(Clips.WorkPool, exclude: null), now);
    }

    void UpdateWork(string? tool, double now)
    {
        if (_workProvisional && SceneForTool(tool) is { } byTool)
        {
            _workProvisional = false;
            SetWorkScene(byTool, now);
            return;
        }
        if (now >= _workUntil)
        {
            _workProvisional = false;
            SetWorkScene(Pick(Clips.WorkPool, exclude: _workScene), now);
        }
    }

    void SetWorkScene(string scene, double now)
    {
        _workScene = scene;
        _workUntil = now + Uniform(8, 18);
        Clip = scene;
    }

    // ---- repos ---------------------------------------------------------------

    void UpdateIdle(double now, double chillDelaySeconds)
    {
        if (_chill == ChillPhase.None)
        {
            if (now - _idleSince < chillDelaySeconds) return;
            StartVariant(now);
            return;
        }
        if (now < _phaseUntil) return;
        switch (_chill)
        {
            case ChillPhase.Variant:
                SetPhase(ChillPhase.Neutral, Clips.Idle, Movement.Hold, now + Uniform(1.5, 3));
                break;
            case ChillPhase.Neutral:
                // la suivante, ou une petite balade entre deux
                if (_rng.NextDouble() < 0.3) SetPhase(ChillPhase.Stroll, Clips.Walk, Movement.Walk, now + Uniform(1.5, 3.5));
                else StartVariant(now);
                break;
            default:
                StartVariant(now);
                break;
        }
    }

    void StartVariant(double now)
    {
        _variant = Pick(Clips.IdlePool, exclude: _lastVariant);
        _lastVariant = _variant;
        // le skate roule vraiment : ses traits de vitesse mentiraient sur place
        bool rolls = _variant == Clips.IdleWheel;
        SetPhase(ChillPhase.Variant, _variant, rolls ? Movement.Walk : Movement.Hold, now + Uniform(6, 15));
        if (rolls) WalkSpeed = 1.6;
    }

    void SetPhase(ChillPhase phase, string clip, Movement movement, double until)
    {
        _chill = phase;
        Clip = clip;
        Movement = movement;
        WalkSpeed = 1.0;
        _phaseUntil = until;
    }

    // ---- tirages -------------------------------------------------------------

    /// <summary>Tirage pondere parmi les clips presents, sans retomber sur celui qu'on quitte.</summary>
    string Pick(IReadOnlyList<(string Name, int Weight)> pool, string? exclude)
    {
        int total = 0;
        foreach (var (name, weight) in pool)
            if (name != exclude && _hasClip(name)) total += weight;
        if (total == 0) return exclude ?? pool[0].Name;
        int r = _rng.Next(total);
        foreach (var (name, weight) in pool)
        {
            if (name == exclude || !_hasClip(name)) continue;
            if (r < weight) return name;
            r -= weight;
        }
        return pool[0].Name;
    }

    double Uniform(double min, double max) => min + _rng.NextDouble() * (max - min);
}
