using Hamster.Activity;

namespace Hamster.App.State;

/// <summary>
/// La sonnerie des attentes. Elle part quand l'ensemble des sessions qui attendent
/// l'utilisateur, la principale et les minis de session, gagne un membre : une seconde
/// conversation qui se met a attendre sonne aussi, et une attente deja annoncee ne resonne
/// pas quand elle passe de mini a principale. Pur calcul : Rings dit si le controleur joue le
/// son (qui verifie encore Son coche et les notifications de Windows).
/// </summary>
internal sealed class WaitingBell
{
    /// <summary>Pas de son pendant l'amorcage : les transcripts relus ne sont pas des nouveautes.</summary>
    public const double QuietStartSeconds = 3;

    readonly HashSet<string> _waiting = new(StringComparer.Ordinal);

    /// <summary>Sessions tenues pour en attente apres le dernier Update.</summary>
    public IReadOnlyCollection<string> Waiting => _waiting;

    /// <summary>
    /// Aucun son, sonnerie comprise : hamster suspendu (pause, veille, cache), hub pas encore
    /// branche, ou moins de QuietStartSeconds depuis son branchement (amorcage). Secondes de
    /// l'horloge du controleur.
    /// </summary>
    public static bool Quiet(bool suspended, double hubStartedAt, double now) =>
        suspended || double.IsNaN(hubStartedAt) || now - hubStartedAt < QuietStartSeconds;

    /// <summary>Sonner pour cet instantane : une attente de plus (Update), hors silence.</summary>
    public static bool Rings(IReadOnlyList<string> joined, bool quiet) => !quiet && joined.Count > 0;

    /// <summary>
    /// Aligne l'ensemble sur l'instantane. Rend les sessions qui viennent de s'y ajouter :
    /// vide, pas de sonnerie. A appeler a chaque instantane, meme quand on ne sonne pas,
    /// pour que les attentes vues pendant l'amorcage ne sonnent pas ensuite.
    /// </summary>
    public IReadOnlyList<string> Update(ActivitySnapshot s)
    {
        var now = new HashSet<string>(StringComparer.Ordinal);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        var joined = new List<string>();
        void See(string id, PetState state)
        {
            visible.Add(id);
            // la fete (3 s) passe devant l'attente sans la terminer : une demande d'un sous-agent
            // pendant la fin du tour du principal reprend ensuite, deja annoncee
            if (state == PetState.Celebrating && _waiting.Contains(id)) now.Add(id);
            if (state != PetState.WaitingUser || !now.Add(id)) return;
            // dans l'ordre de l'instantane : principale d'abord
            if (!_waiting.Contains(id)) joined.Add(id);
        }

        if (s.MainSessionId is { } main) See(main, s.State);
        foreach (var m in s.Minis)
            if (m.Kind == "session") See(m.Id, m.State);

        // ActivityModel.Snapshot met les attentes en tete des minis : une conversation qui attend
        // n'est coupee de l'instantane que si toutes les places sont prises par des attentes.
        // Alors seulement, une attente deja annoncee et absente est gardee, pour ne pas resonner
        // quand elle revient. Hors de ce cas, absente veut dire finie : sa prochaine attente sonne
        if (s.MinisOverflow > 0 && s.Minis.Count > 0 && s.Minis.All(IsWaitingSession))
            foreach (var id in _waiting)
                if (!visible.Contains(id)) now.Add(id);

        _waiting.Clear();
        _waiting.UnionWith(now);
        return joined;
    }

    static bool IsWaitingSession(MiniInfo m) => m.Kind == "session" && m.State == PetState.WaitingUser;
}
