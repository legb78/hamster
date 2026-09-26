namespace Hamster.Activity;

/// <summary>
/// Machine a etats des sessions Claude Code. Aucune horloge systeme : le temps vient des
/// evenements et du parametre now de Snapshot, ce qui rend chaque regle testable.
/// Apply et Snapshot peuvent etre appeles depuis des threads differents (verrou interne).
/// </summary>
public sealed class ActivityModel
{
    /// <summary>Plafond de minis a l'ecran ; le reste est compte dans MinisOverflow.</summary>
    public const int MaxMinis = 6;

    // cle du fil principal d'une session ; les sous-agents sont designes par leur agentId
    const string MainThread = "";

    readonly ActivityOptions _o;
    readonly object _gate = new();
    readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    string? _mainId;
    /// <summary>
    /// Attentes perimees qui ont cede la place (session, WaitSince). Tant que la meme attente
    /// dure, elle ne redevient candidate ni comme attente ni sans principale : elle reste en mini.
    /// </summary>
    readonly Dictionary<string, DateTimeOffset> _yielded = new(StringComparer.Ordinal);

    public ActivityModel(ActivityOptions? options = null)
    {
        _o = options ?? new ActivityOptions();
    }

    enum Wait { None, Asked, Needs }

    sealed record Pending(string? Id, string Name, DateTimeOffset Since);

    sealed class Agent(string id)
    {
        public readonly string Id = id;
        public string? Label;
        /// <summary>Agent au premier plan : id du tool_use Agent du fil principal, dont le resultat le termine.</summary>
        public string? ForegroundToolUseId;
        public bool Active;
        public DateTimeOffset Since, LastEvent;
        public DateTimeOffset? EndedAt;
        public DateTimeOffset ErrorUntil;
        public readonly List<Pending> Tools = new();
    }

    sealed class Session(string id)
    {
        public readonly string Id = id;
        public string? Cwd;
        /// <summary>Tout evenement, sous-agents et hook compris : sert a oublier la session.</summary>
        public DateTimeOffset LastEvent;
        /// <summary>Evenements du fil principal seulement : sert au garde-fou WorkingTimeout.</summary>
        public DateTimeOffset LastMainActivity;
        /// <summary>Un tour est en cours sur le fil principal.</summary>
        public bool Busy;
        /// <summary>Derniere fin de tour du fil principal.</summary>
        public DateTimeOffset TurnEndedAt;
        public DateTimeOffset TurnStart;
        public int TurnTools;
        public DateTimeOffset ActiveSince;
        public Wait Wait;
        public DateTimeOffset WaitSince;
        /// <summary>Fil dont on attend le prochain evenement pour lever l'attente ; null = n'importe lequel.</summary>
        public string? WaitThread;
        public DateTimeOffset ErrorUntil, CelebrateUntil;
        public readonly List<Pending> Tools = new();
        public readonly Dictionary<string, Agent> Agents = new(StringComparer.Ordinal);
    }

    public void Apply(ActivityEvent e)
    {
        if (e is null || string.IsNullOrEmpty(e.SessionId)) return;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(e.SessionId, out var s))
            {
                // une fin de tache seule (Bash de fond, agent deja oublie) ne fait pas exister une
                // session. Une demande du hook non plus : sans aucun evenement de transcript pour
                // cette session, rien ne leverait jamais son attente (session non enregistree,
                // transcripts hors du dossier suivi)
                if (e.Kind is ActivityKind.SubagentEnded or ActivityKind.NeedsUser
                    || (e.Kind == ActivityKind.TurnEnded && e.AgentId != null)) return;
                s = new Session(e.SessionId);
                _sessions.Add(e.SessionId, s);
            }
            // apres un long silence, la session repart de zero meme si un tour etait reste ouvert
            bool wasEngaged = Engaged(s) && e.Time - s.LastEvent <= _o.WorkingTimeout;
            if (e.Time > s.LastEvent) s.LastEvent = e.Time;

            if (e.Kind == ActivityKind.NeedsUser) OnNeedsUser(s, e);
            else
            {
                bool wasWaiting = s.Wait != Wait.None;
                ClearAnsweredWait(s, e);
                if (e.AgentId != null) OnAgentEvent(s, e);
                else OnMainEvent(s, e, wasWaiting);
            }
            // debut d'une periode d'activite : c'est l'age du mini de cette session
            if (!wasEngaged && Engaged(s)) s.ActiveSince = e.Time;
        }
    }

    /// <summary>
    /// Vrai si un transcript a deja donne un evenement pour cette session (et qu'elle n'est
    /// pas oubliee). Sans verrou externe : utilisable comme HookEventsWatcher.KnownSession.
    /// </summary>
    public bool KnowsSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_gate) return _sessions.ContainsKey(sessionId);
    }

    /// <summary>Activite sans tenir compte des delais : tour en cours, attente ou sous-agent.</summary>
    static bool Engaged(Session s) => s.Busy || s.Wait != Wait.None || s.Agents.Values.Any(a => a.Active);

    /// <summary>
    /// La notification ne dit pas quel fil attend (pas d'agent_id pour Notification dans la
    /// version observee) : on prend celui dont l'outil en suspens a demarre le plus
    /// recemment, puisque la demande d'autorisation suit de pres le tool_use qu'elle bloque.
    /// Sans cela, un sous-agent de fond qui travaille leverait l'attente aussitot.
    /// </summary>
    void OnNeedsUser(Session s, ActivityEvent e)
    {
        string? thread = e.AgentId ?? BlockedThread(s, e.Time);
        // question ou plan du meme fil (AskUserQuestion, ExitPlanMode) : d'apres le binaire de
        // Claude Code 2.1.283 (deduction, non observee), ces dialogues emettent aussi une
        // Notification permission_prompt quelques secondes apres. C'est la meme attente : elle
        // garde son heure et son delai d'1 h, au lieu de retomber aux 10 min du hook
        if (s.Wait == Wait.Asked && thread == s.WaitThread) return;
        s.Wait = Wait.Needs;
        s.WaitSince = e.Time;
        s.WaitThread = thread;
        if (s.Cwd == null && e.Cwd != null) s.Cwd = e.Cwd;
    }

    static string? BlockedThread(Session s, DateTimeOffset at)
    {
        string? best = null;
        DateTimeOffset bestTime = DateTimeOffset.MinValue;
        foreach (var p in s.Tools)
            if (p.Since <= at && p.Since > bestTime) { best = MainThread; bestTime = p.Since; }
        foreach (var a in s.Agents.Values)
        {
            if (!a.Active) continue;
            foreach (var p in a.Tools)
                if (p.Since <= at && p.Since > bestTime) { best = a.Id; bestTime = p.Since; }
        }
        return best;
    }

    void ClearAnsweredWait(Session s, ActivityEvent e)
    {
        string thread = e.AgentId ?? MainThread;
        switch (s.Wait)
        {
            case Wait.Asked:
                // la reponse arrive en tool_result sur le fil qui a pose la question. Un autre
                // tool_use du meme message (appels paralleles) ne vaut pas reponse
                if (thread == s.WaitThread && e.Time >= s.WaitSince
                    && e.Kind is not (ActivityKind.ToolStarted or ActivityKind.AskedUser))
                    s.Wait = Wait.None;
                break;
            case Wait.Needs:
                // seul un evenement de transcript posterieur a la notification compte : les lignes
                // ecrites avant, mais lues apres, ne disent rien de la reponse
                if (e.Time > s.WaitSince
                    && (s.WaitThread == null || thread == s.WaitThread
                        || (thread == MainThread && e.Kind == ActivityKind.PromptSubmitted)))
                    s.Wait = Wait.None;
                break;
        }
    }

    void OnMainEvent(Session s, ActivityEvent e, bool wasWaiting)
    {
        if (e.Cwd != null) s.Cwd = e.Cwd;
        var t = e.Time;
        switch (e.Kind)
        {
            case ActivityKind.PromptSubmitted:
            case ActivityKind.SessionActivity:
                BeginWork(s, t, e.Kind, wasWaiting);
                break;
            case ActivityKind.ToolStarted:
                BeginWork(s, t, e.Kind, wasWaiting);
                s.TurnTools++;
                s.Tools.Add(new Pending(e.Detail, e.ToolName ?? "?", t));
                break;
            case ActivityKind.AskedUser:
                BeginWork(s, t, e.Kind, wasWaiting);
                s.Tools.Add(new Pending(e.Detail, e.ToolName ?? "?", t));
                s.Wait = Wait.Asked;
                s.WaitSince = t;
                s.WaitThread = MainThread;
                break;
            case ActivityKind.ToolFinished:
                BeginWork(s, t, e.Kind, wasWaiting);
                Finish(s.Tools, e.Detail);
                if (e.IsError) s.ErrorUntil = Later(s.ErrorUntil, t + _o.ErrorDuration);
                // resultat de l'outil Agent d'un sous-agent au premier plan : il a fini, meme si
                // son transcript s'arrete sur un stop_reason null (erreur ou interruption comprises)
                if (e.Detail != null)
                    foreach (var a in s.Agents.Values)
                        if (a.ForegroundToolUseId == e.Detail) End(a, t);
                break;
            case ActivityKind.ApiError:
                // ligne system api_error datee d'avant la fin du tour mais ecrite apres elle (la
                // relecture des vrais transcripts en compte) : elle ne rouvre pas un tour fini.
                // Un sous-agent a deja cette regle : une ligne anterieure a sa fin est ignoree
                if (t <= s.TurnEndedAt) return;
                BeginWork(s, t, e.Kind, wasWaiting);
                s.ErrorUntil = Later(s.ErrorUntil, t + _o.ErrorDuration);
                break;
            case ActivityKind.TurnEnded:
                EndTurn(s, e);
                break;
            default:
                // SubagentActivity / SubagentEnded sans agentId : rien a rattacher
                return;
        }
        if (t > s.LastMainActivity) s.LastMainActivity = t;
    }

    void BeginWork(Session s, DateTimeOffset t, ActivityKind kind, bool wasWaiting)
    {
        // un nouveau prompt apres un tour reste ouvert et muet (Claude Code tue, fin jamais ecrite)
        // ouvre un nouveau tour ; un resultat d'outil tardif, lui, continue le tour en cours
        bool stale = s.Busy && kind == ActivityKind.PromptSubmitted && !wasWaiting
                     && t - s.LastMainActivity > _o.WorkingTimeout;
        if (s.Busy && !stale) return;
        s.Busy = true;
        s.TurnStart = t;
        s.TurnTools = 0;
        if (stale) s.Tools.Clear();
    }

    void EndTurn(Session s, ActivityEvent e)
    {
        if (e.Time > s.TurnEndedAt) s.TurnEndedAt = e.Time;
        // deuxieme fin du meme message (bloc thinking puis bloc text, tous deux en end_turn)
        if (!s.Busy) return;
        bool normal = e.Detail == null;
        if (normal && (s.TurnTools > 0 || e.Time - s.TurnStart >= _o.MinTurnForCelebration))
            s.CelebrateUntil = Later(s.CelebrateUntil, e.Time + _o.CelebrateDuration);
        s.Busy = false;
        s.Tools.Clear();
        if (s.Wait == Wait.Asked) s.Wait = Wait.None;
    }

    void OnAgentEvent(Session s, ActivityEvent e)
    {
        string id = e.AgentId!;
        s.Agents.TryGetValue(id, out var a);
        var t = e.Time;
        if (e.Kind is ActivityKind.SubagentEnded or ActivityKind.TurnEnded)
        {
            // fin d'un agent encore inconnu (task-notification du parent lue avant les lignes de
            // l'agent) : il nait fini, et ses lignes plus anciennes ne le ranimeront pas. Pour un
            // Bash de fond, c'est une fiche inactive de plus, oubliee avec les autres
            if (a == null)
            {
                a = new Agent(id) { Since = t };
                s.Agents.Add(id, a);
            }
            if (e.ForegroundToolUseId != null) a.ForegroundToolUseId = e.ForegroundToolUseId;
            End(a, t);
            return;
        }
        if (a == null)
        {
            a = new Agent(id);
            s.Agents.Add(id, a);
        }
        if (e.ForegroundToolUseId != null) a.ForegroundToolUseId = e.ForegroundToolUseId;
        if (t > a.LastEvent) a.LastEvent = t;
        if (e.Kind == ActivityKind.SubagentActivity && !string.IsNullOrWhiteSpace(e.Detail)) a.Label = e.Detail;

        // une ligne anterieure a la fin connue, lue apres elle depuis un autre fichier (la
        // task-notification du parent passe parfois avant la derniere ligne de l'agent),
        // ne ressuscite pas l'agent
        if (a.EndedAt is { } ended && t <= ended) return;
        if (!a.Active)
        {
            a.Active = true;
            a.Since = t;
            a.EndedAt = null;
        }
        switch (e.Kind)
        {
            case ActivityKind.ToolStarted:
            case ActivityKind.AskedUser:
                a.Tools.Add(new Pending(e.Detail, e.ToolName ?? "?", t));
                break;
            case ActivityKind.ToolFinished:
                string? name = Finish(a.Tools, e.Detail);
                if (e.IsError) a.ErrorUntil = Later(a.ErrorUntil, t + _o.ErrorDuration);
                // les agents de workflow finissent sur le resultat de StructuredOutput, sans end_turn
                else if (name == "StructuredOutput") End(a, t);
                break;
            case ActivityKind.ApiError:
                a.ErrorUntil = Later(a.ErrorUntil, t + _o.ErrorDuration);
                break;
        }
    }

    static void End(Agent a, DateTimeOffset t)
    {
        a.Active = false;
        a.Tools.Clear();
        if (a.EndedAt is not { } ended || t > ended) a.EndedAt = t;
        if (t > a.LastEvent) a.LastEvent = t;
    }

    /// <summary>Retire l'outil fini ; sans id, le plus ancien. Rend son nom.</summary>
    static string? Finish(List<Pending> tools, string? id)
    {
        int i = id == null ? (tools.Count > 0 ? 0 : -1) : tools.FindIndex(p => p.Id == id);
        if (i < 0) return null;
        string name = tools[i].Name;
        tools.RemoveAt(i);
        return name;
    }

    static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    /// <summary>Vue calculee d'une session a l'instant now. State null = inactive.</summary>
    readonly record struct View(Session Session, PetState? State, bool Active);

    public ActivitySnapshot Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            Forget(now);
            var views = new List<View>(_sessions.Count);
            foreach (var s in _sessions.Values) views.Add(ViewOf(s, now));

            var main = PickMain(views, now);
            _mainId = main?.Session.Id;

            // WaitSince pour une conversation en attente, null pour les autres
            var all = new List<(MiniInfo Info, DateTimeOffset? Wait)>();
            foreach (var v in views)
            {
                foreach (var a in v.Session.Agents.Values)
                    if (AgentActive(a, now))
                        all.Add((new MiniInfo(a.Id, "subagent", a.Label ?? "sous-agent", a.Id,
                            now < a.ErrorUntil ? PetState.Error : PetState.Working, a.Since), null));
                if (v.State is { } state && v.Session.Id != _mainId)
                    all.Add((new MiniInfo(v.Session.Id, "session", FolderName(v.Session.Cwd) ?? "session",
                        v.Session.Cwd ?? v.Session.Id, state, v.Session.ActiveSince),
                        state == PetState.WaitingUser ? v.Session.WaitSince : null));
            }
            // les conversations en attente d'abord, la plus recente attente en tete : coupee de
            // l'instantane, une attente n'aurait ni mini, ni etiquette, ni sonnerie, et c'est la
            // derniere venue qui doit sonner. Puis les plus recents d'abord ; l'id departage, pour
            // un ordre stable d'une image a l'autre
            all.Sort((x, y) =>
            {
                if (x.Wait.HasValue != y.Wait.HasValue) return x.Wait.HasValue ? -1 : 1;
                int c = x.Wait is { } xw ? y.Wait!.Value.CompareTo(xw) : y.Info.Since.CompareTo(x.Info.Since);
                return c != 0 ? c : string.CompareOrdinal(x.Info.Id, y.Info.Id);
            });
            int overflow = Math.Max(0, all.Count - MaxMinis);
            var minis = new List<MiniInfo>(Math.Min(all.Count, MaxMinis));
            for (int i = 0; i < all.Count && i < MaxMinis; i++) minis.Add(all[i].Info);

            bool anyActive = views.Any(v => v.Active);
            if (main is not { } m)
                return new ActivitySnapshot(PetState.Idle, null, null, null, anyActive, minis, overflow);

            var ms = m.Session;
            string? tool = ms.Busy && ms.Tools.Count > 0 ? ms.Tools[^1].Name : null;
            return new ActivitySnapshot(m.State ?? PetState.Idle, ms.Id, FolderName(ms.Cwd), tool, anyActive, minis, overflow);
        }
    }

    void Forget(DateTimeOffset now)
    {
        List<string>? gone = null;
        foreach (var s in _sessions.Values)
        {
            if (now - s.LastEvent > _o.ForgetAfter)
            {
                (gone ??= new()).Add(s.Id);
                continue;
            }
            if (s.Wait != Wait.None && now - s.WaitSince > WaitTimeout(s.Wait)) s.Wait = Wait.None;
            List<string>? oldAgents = null;
            foreach (var a in s.Agents.Values)
                if (now - a.LastEvent > _o.ForgetAfter) (oldAgents ??= new()).Add(a.Id);
            if (oldAgents != null) foreach (var id in oldAgents) s.Agents.Remove(id);
        }
        if (gone != null) foreach (var id in gone) _sessions.Remove(id);
    }

    /// <summary>
    /// Question ou plan (Asked) : visibles dans le transcript, attente longue. Demande du hook
    /// (Needs) : rien ne s'ecrit entre l'autorisation accordee et le tool_result, ni apres une
    /// session tuee pendant l'attente, attente courte.
    /// </summary>
    TimeSpan WaitTimeout(Wait w) => w == Wait.Needs ? _o.NeedsUserTimeout : _o.WaitingTimeout;

    /// <summary>
    /// Muet avec un outil en suspens : l'outil peut etre long, SubagentTimeout. Muet sans outil
    /// en suspens : comme le fil principal, WorkingTimeout (Claude Code ferme en plein travail).
    /// </summary>
    bool AgentActive(Agent a, DateTimeOffset now) =>
        a.Active && now - a.LastEvent <= (a.Tools.Count > 0 ? _o.SubagentTimeout : _o.WorkingTimeout);

    View ViewOf(Session s, DateTimeOffset now)
    {
        bool agents = s.Agents.Values.Any(a => AgentActive(a, now));
        bool working = s.Busy && now - s.LastMainActivity <= _o.WorkingTimeout;
        bool waiting = s.Wait != Wait.None && now - s.WaitSince <= WaitTimeout(s.Wait);
        bool active = waiting || working || agents;

        PetState? state =
            now < s.CelebrateUntil ? PetState.Celebrating
            : waiting ? PetState.WaitingUser
            : now < s.ErrorUntil ? PetState.Error
            : working || agents ? PetState.Working
            : null;
        return new View(s, state, active);
    }

    /// <summary>
    /// Session principale collante : on la garde tant qu'elle est active ou dans un transitoire.
    /// Une autre qui attend l'utilisateur prend sa place si cette attente est fraiche : moins de
    /// FreshWaitWindow, ou commencee apres la derniere activite de la principale sans qu'une
    /// session qui travaille se soit manifestee depuis. Une principale qui attend depuis plus
    /// longtemps cede la place a une attente fraiche, sinon a une session active qui s'est
    /// manifestee depuis le debut de son attente (session tuee pendant l'attente, outil
    /// autorise qui tourne sans rien ecrire). Sans principale : l'attente fraiche la plus
    /// recente, sinon la plus recente des actives, avec la meme regle si elle attend depuis
    /// longtemps. Chaque changement est a sens unique : la session quittee ne remplit plus la
    /// condition qui la ferait revenir, donc pas de va-et-vient d'une image a l'autre. Deux
    /// regles le garantissent quand le changement vient du temps et non d'un evenement : une
    /// session ne prend la place que si elle doit rester active au moins SuccessorMargin (sinon
    /// elle la rendrait aussitot) ; une attente perimee qui a cede la place ne la reprend plus,
    /// ni comme attente ni sans principale, tant que dure la meme attente : elle reste en mini.
    /// </summary>
    View? PickMain(List<View> views, DateTimeOffset now)
    {
        var freshSince = now - _o.FreshWaitWindow;
        DropEndedYields();
        View? current = null;
        foreach (var v in views)
            if (v.Session.Id == _mainId && v.State != null) current = v;

        View? waiting = null;
        foreach (var v in views)
        {
            if (v.State != PetState.WaitingUser || v.Session.Id == current?.Session.Id
                || _yielded.ContainsKey(v.Session.Id) || !Lasts(v.Session, now)) continue;
            var since = v.Session.WaitSince;
            bool fresh = since >= freshSince
                || (current is { } cur && since > cur.Session.LastEvent && Successor(views, v, now) == null);
            if (fresh && (waiting is not { } w || since > w.Session.WaitSince)) waiting = v;
        }

        if (current is { } c)
        {
            if (c.State != PetState.WaitingUser) return waiting ?? c;
            if (c.Session.WaitSince >= freshSince) return c;
            return YieldTo(c, waiting ?? Successor(views, c, now));
        }
        if (waiting != null) return waiting;

        View? latest = null;
        foreach (var v in views)
            if (v.Active && !_yielded.ContainsKey(v.Session.Id)
                && (latest is not { } l || v.Session.LastEvent > l.Session.LastEvent))
                latest = v;
        if (latest is { } last && last.State == PetState.WaitingUser && last.Session.WaitSince < freshSince)
            return YieldTo(last, Successor(views, last, now));
        return latest;
    }

    /// <summary>La place va a next s'il existe, et l'attente w, qui la cede, est notee ; sinon w la garde.</summary>
    View YieldTo(View w, View? next)
    {
        if (next is not { } n) return w;
        _yielded[w.Session.Id] = w.Session.WaitSince;
        return n;
    }

    /// <summary>Oublie les attentes cedees qui ont pris fin : session oubliee, attente levee ou remplacee par une autre.</summary>
    void DropEndedYields()
    {
        if (_yielded.Count == 0) return;
        List<string>? ended = null;
        foreach (var (id, since) in _yielded)
            if (!_sessions.TryGetValue(id, out var s) || s.Wait == Wait.None || s.WaitSince != since)
                (ended ??= new()).Add(id);
        if (ended != null) foreach (var id in ended) _yielded.Remove(id);
    }

    /// <summary>
    /// La plus recente des sessions actives hors attente qui se sont manifestees depuis le
    /// debut de l'attente de w et resteront actives au moins SuccessorMargin, ou null.
    /// </summary>
    View? Successor(List<View> views, View w, DateTimeOffset now)
    {
        View? best = null;
        foreach (var v in views)
            if (v.Active && v.State != PetState.WaitingUser && v.Session.Id != w.Session.Id
                && v.Session.LastEvent > w.Session.WaitSince && Lasts(v.Session, now)
                && (best is not { } b || v.Session.LastEvent > b.Session.LastEvent))
                best = v;
        return best;
    }

    /// <summary>Vrai si la session restera active au moins SuccessorMargin sans nouvel evenement.</summary>
    bool Lasts(Session s, DateTimeOffset now) => ActiveUntil(s) >= now + _o.SuccessorMargin;

    /// <summary>
    /// Dernier instant ou la session est encore active sans nouvel evenement, avec les delais de
    /// ViewOf : attente, tour du fil principal, et chacun de ses sous-agents actifs.
    /// </summary>
    DateTimeOffset ActiveUntil(Session s)
    {
        var until = DateTimeOffset.MinValue;
        if (s.Wait != Wait.None) until = Later(until, s.WaitSince + WaitTimeout(s.Wait));
        if (s.Busy) until = Later(until, s.LastMainActivity + _o.WorkingTimeout);
        foreach (var a in s.Agents.Values)
            if (a.Active) until = Later(until, a.LastEvent + (a.Tools.Count > 0 ? _o.SubagentTimeout : _o.WorkingTimeout));
        return until;
    }

    static string? FolderName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        string trimmed = cwd.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return cwd;
        int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        string name = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return name.Length > 0 ? name : trimmed;
    }
}
