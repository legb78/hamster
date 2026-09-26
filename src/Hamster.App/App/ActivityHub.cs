using Hamster.Activity;

namespace Hamster.App;

/// <summary>
/// Branche Hamster.Activity sur l'interface : possede les deux watchers et le modele, ramene
/// leurs lots sur le thread UI (ils arrivent sur des threads de fond), et publie un instantane
/// a chaque lot et a chaque tick d'entretien. Tout ce qui suit Changed tourne donc sur le
/// thread UI, sans verrou.
/// </summary>
internal sealed class ActivityHub : IDisposable
{
    readonly Control _ui;
    readonly ActivityModel _model = new();
    readonly TranscriptWatcher _transcripts;
    readonly HookEventsWatcher _hooks;
    volatile bool _disposed;
    ActivitySnapshot _snapshot = new(PetState.Idle, null, null, null, false, Array.Empty<MiniInfo>(), 0);

    public ActivityHub(Control ui)
    {
        _ui = ui;
        ProjectsRoot = TranscriptPaths.DefaultProjectsRoot;
        EventsPath = HookEventsWatcher.DefaultEventsPath;
        _transcripts = new TranscriptWatcher(ProjectsRoot);
        _hooks = new HookEventsWatcher(EventsPath);
    }

    public string ProjectsRoot { get; }
    public string EventsPath { get; }
    public ActivitySnapshot Snapshot => _snapshot;
    public string TranscriptStatus => _transcripts.Status;
    public string HookStatus => _hooks.Status;
    public long EventsApplied { get; private set; }

    /// <summary>Leve sur le thread UI : (instantane precedent, nouvel instantane).</summary>
    public event Action<ActivitySnapshot, ActivitySnapshot>? Changed;

    public void Start()
    {
        // le script est depose, jamais branche : les reglages de Claude Code restent a l'utilisateur
        try { Diagnostics.Info("hook: script pret dans " + HookInstaller.EnsureHookScript()); }
        catch (Exception e) { Diagnostics.Warn("hook: script non depose: " + e.Message); }

        _transcripts.Events += OnEvents;
        _hooks.Events += OnEvents;
        _transcripts.Start();
        _hooks.Start();
        Diagnostics.Info($"activite: transcripts {ProjectsRoot}, hook {EventsPath}");
    }

    /// <summary>Thread de fond : on ne touche a rien ici, on repasse sur le thread UI.</summary>
    void OnEvents(IReadOnlyList<ActivityEvent> batch)
    {
        if (_disposed || batch.Count == 0) return;
        try { _ui.BeginInvoke(new Action(() => Apply(batch))); }
        // fenetre detruite pendant l'arret : le lot n'a plus personne a qui parler
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
    }

    void Apply(IReadOnlyList<ActivityEvent> batch)
    {
        if (_disposed) return;
        foreach (var e in batch) _model.Apply(e);
        EventsApplied += batch.Count;
        Refresh();
    }

    /// <summary>Recalcule l'instantane maintenant. Appele a chaque lot et a chaque tick d'entretien.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        var next = _model.Snapshot(DateTimeOffset.UtcNow);
        var previous = _snapshot;
        _snapshot = next;
        LogChanges(previous, next);
        Changed?.Invoke(previous, next);
    }

    // ---- journal -----------------------------------------------------------------

    static void LogChanges(ActivitySnapshot a, ActivitySnapshot b)
    {
        if (a.State != b.State || a.MainSessionId != b.MainSessionId)
            Diagnostics.Info($"etat {a.State} -> {b.State}" +
                (b.MainSessionId == null ? "" : $", projet {b.MainLabel ?? "?"} (session {Short(b.MainSessionId)})") +
                (b.Tool == null ? "" : $", outil {b.Tool}"));
        else if (a.Tool != b.Tool && b.Tool != null)
            Diagnostics.Info($"outil {a.Tool ?? "-"} -> {b.Tool}");

        foreach (var m in b.Minis)
        {
            var before = a.Minis.FirstOrDefault(x => x.Id == m.Id);
            if (before == null) Diagnostics.Info($"mini + {m.Kind} {Name(m)} \"{m.Label}\" ({m.State})");
            else if (before.State != m.State) Diagnostics.Info($"mini {Name(m)} {before.State} -> {m.State}");
        }
        foreach (var m in a.Minis)
            if (!b.Minis.Any(x => x.Id == m.Id)) Diagnostics.Info($"mini - {m.Kind} {Name(m)}");
        if (a.MinisOverflow != b.MinisOverflow) Diagnostics.Info($"minis en trop: {b.MinisOverflow}");
    }

    static string Short(string id) => id.Length <= 8 ? id : id[..8];

    /// <summary>Un sous-agent en entier (ses ids sont courts et partagent souvent un prefixe), une session en abrege.</summary>
    static string Name(MiniInfo m) => m.Kind == "subagent" ? m.Id : Short(m.Id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transcripts.Events -= OnEvents;
        _hooks.Events -= OnEvents;
        _transcripts.Dispose();
        _hooks.Dispose();
    }
}
