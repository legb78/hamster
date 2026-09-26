using Hamster.Activity;

namespace Hamster.App.State;

/// <summary>
/// La sonnerie des attentes. Elle part quand l'ensemble des sessions qui attendent
/// l'utilisateur, la principale et les minis de session, gagne un membre : une seconde
/// conversation qui se met a attendre sonne aussi, et une attente deja annoncee ne resonne
/// pas quand elle passe de mini a principale. Pur calcul : le controleur decide s'il joue
/// le son (Son coche, pas pendant l'amorcage ni en pause).
/// </summary>
internal sealed class WaitingBell
{
    readonly HashSet<string> _waiting = new(StringComparer.Ordinal);

    /// <summary>Sessions tenues pour en attente apres le dernier Update.</summary>
    public IReadOnlyCollection<string> Waiting => _waiting;

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
        void See(string id, bool waiting)
        {
            visible.Add(id);
            if (!waiting || !now.Add(id)) return;
            // dans l'ordre de l'instantane : principale d'abord
            if (!_waiting.Contains(id)) joined.Add(id);
        }

        if (s.MainSessionId is { } main) See(main, s.State == PetState.WaitingUser);
        foreach (var m in s.Minis)
            if (m.Kind == "session") See(m.Id, m.State == PetState.WaitingUser);

        // au-dela de six minis, une session peut sortir de l'instantane sans avoir fini
        // d'attendre : elle y revient au gre des sous-agents qui passent. On la garde tant
        // qu'on ne l'a pas revue dans un autre etat, sinon elle resonnerait a chaque retour
        if (s.MinisOverflow > 0)
            foreach (var id in _waiting)
                if (!visible.Contains(id)) now.Add(id);

        _waiting.Clear();
        _waiting.UnionWith(now);
        return joined;
    }
}
