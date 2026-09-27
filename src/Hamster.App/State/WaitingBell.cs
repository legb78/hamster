using Hamster.Activity;

namespace Hamster.App.State;

/// <summary>
/// La sonnerie des attentes. Elle part quand une session, la principale ou un mini de session,
/// se met au telephone avec une attente pas encore annoncee : une seconde conversation qui se
/// met a attendre sonne aussi. Une attente se reconnait a son debut (WaitSince) : deja annoncee,
/// elle ne resonne pas tant que ce debut ne change pas, ni quand elle passe de mini a principale,
/// ni apres la fete de 3 s qui passe devant elle, ni quand elle revient apres avoir ete coupee de
/// l'instantane. Pur calcul : Rings dit si le controleur joue le son (qui verifie encore Son
/// coche et les notifications de Windows).
/// </summary>
internal sealed class WaitingBell
{
    /// <summary>Pas de son pendant l'amorcage : les transcripts relus ne sont pas des nouveautes.</summary>
    public const double QuietStartSeconds = 3;

    /// <summary>
    /// Attentes retenues au plus. Au-dela, la plus ancienne est oubliee : il faudrait plus
    /// d'attentes cachees a la fois que ce plafond pour qu'une d'elles resonne.
    /// </summary>
    public const int MaxRemembered = 64;

    /// <summary>Attentes deja annoncees, visibles ou non : session, debut de l'attente.</summary>
    readonly Dictionary<string, DateTimeOffset> _announced = new(StringComparer.Ordinal);

    /// <summary>Sessions au telephone dans le dernier instantane.</summary>
    HashSet<string> _shown = new(StringComparer.Ordinal);

    /// <summary>Sessions dont l'attente est deja annoncee apres le dernier Update, visibles ou non.</summary>
    public IReadOnlyCollection<string> Waiting => _announced.Keys;

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
    /// Note les attentes de l'instantane. Rend les sessions qui viennent de se mettre au
    /// telephone avec une attente pas encore annoncee : vide, pas de sonnerie. A appeler a chaque
    /// instantane, meme quand on ne sonne pas, pour que les attentes vues pendant l'amorcage ne
    /// sonnent pas ensuite.
    /// </summary>
    public IReadOnlyList<string> Update(ActivitySnapshot s)
    {
        var joined = new List<string>();
        var shown = new HashSet<string>(StringComparer.Ordinal);
        void See(string id, PetState state, DateTimeOffset? waitSince)
        {
            // visible sans attente en cours : la sienne est finie, la prochaine aura un autre debut
            if (waitSince == null && state != PetState.WaitingUser)
            {
                _announced.Remove(id);
                return;
            }
            // la fete passe devant l'attente sans la terminer : elle sonnera, si elle est nouvelle,
            // quand le telephone se montrera
            if (state != PetState.WaitingUser || !shown.Add(id)) return;
            // un instantane sans debut (fabrique a la main) vaut une attente sans date
            var since = waitSince ?? DateTimeOffset.MinValue;
            bool known = _announced.TryGetValue(id, out var announced) && announced == since;
            _announced[id] = since;
            // deja au telephone a l'instantane precedent : la meme conversation, sans nouvelle
            // sonnerie, meme si une demande a pris la suite de la sienne sans pause entre les deux
            if (known || _shown.Contains(id)) return;
            // dans l'ordre de l'instantane : principale d'abord
            joined.Add(id);
        }

        if (s.MainSessionId is { } main) See(main, s.State, s.MainWaitSince);
        foreach (var m in s.Minis)
            if (m.Kind == "session") See(m.Id, m.State, m.WaitSince);

        // une attente absente de l'instantane (coupee par six attentes plus recentes) est gardee :
        // elle ne resonne pas en revenant, tant que son debut ne change pas. Seul le plafond en oublie
        while (_announced.Count > MaxRemembered)
            _announced.Remove(_announced.MinBy(kv => kv.Value).Key);
        _shown = shown;
        return joined;
    }
}
