using K = Hamster.Activity.ActivityKind;

namespace Hamster.Activity.Tests;

static class ModelTests
{
    static readonly DateTimeOffset T0 = Jsonl.T0;
    const string A = "session-a", B = "session-b", C = "session-c";

    static DateTimeOffset At(double s) => T0.AddSeconds(s);

    static ActivityEvent E(K kind, double at, string session = A, string? agent = null, string? tool = null,
        string? detail = null, bool error = false, string? cwd = @"C:\work\projet-a") =>
        new(At(at), kind, session, agent, cwd, tool, error, detail);

    /// <summary>
    /// Applique les evenements en prenant un instantane a l'heure de chacun, comme la boucle
    /// de l'app (10 images/s) : la session principale se choisit pendant le tour, pas apres.
    /// </summary>
    static ActivityModel M(params ActivityEvent[] events)
    {
        var m = new ActivityModel();
        foreach (var e in events)
        {
            m.Apply(e);
            m.Snapshot(e.Time);
        }
        return m;
    }

    static PetState S(ActivityModel m, double at) => m.Snapshot(At(at)).State;

    public static void Run()
    {
        T.Suite("ActivityModel");

        T.Case("tour commence et fini entre deux instantanes : rien d'actif, donc Idle", () =>
        {
            // regle du contrat : sans session active ni principale en cours, pas de fete orpheline
            var m = new ActivityModel();
            foreach (var e in new[] { E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.TurnEnded, 2) }) m.Apply(e);
            var s = m.Snapshot(At(2.5));
            T.Eq(PetState.Idle, s.State, "etat");
            T.Eq<string?>(null, s.MainSessionId, "principale");
        });

        T.Case("aucun evenement : Idle, aucune session", () =>
        {
            var s = new ActivityModel().Snapshot(T0);
            T.Eq(PetState.Idle, s.State, "etat");
            T.Eq<string?>(null, s.MainSessionId, "principale");
            T.Eq(false, s.AnySessionActive, "active");
            T.Eq(0, s.Minis.Count, "minis");
        });

        T.Case("PromptSubmitted, ToolStarted, ToolFinished, SessionActivity => Working", () =>
        {
            foreach (var k in new[] { K.PromptSubmitted, K.ToolStarted, K.ToolFinished, K.SessionActivity })
            {
                var m = M(E(k, 0, tool: "Bash", detail: "t1"));
                T.Eq(PetState.Working, S(m, 1), k.ToString());
            }
        });

        T.Case("Tool = dernier outil demarre et pas fini ; MainLabel = dossier du cwd", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Read", detail: "r"), E(K.ToolStarted, 2, tool: "Bash", detail: "b"));
            var s = m.Snapshot(At(3));
            T.Eq("Bash", s.Tool, "outil courant");
            T.Eq("projet-a", s.MainLabel, "libelle");
            T.Eq(A, s.MainSessionId, "session");
            m.Apply(E(K.ToolFinished, 4, detail: "b"));
            T.Eq("Read", m.Snapshot(At(5)).Tool, "le Bash fini, reste le Read");
            m.Apply(E(K.ToolFinished, 6, detail: "r"));
            T.Eq<string?>(null, m.Snapshot(At(7)).Tool, "plus rien en cours");
        });

        T.Case("AskedUser => WaitingUser jusqu'au prochain evenement de la session", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.AskedUser, 1, tool: "AskUserQuestion", detail: "q"));
            T.Eq(PetState.WaitingUser, S(m, 2), "attente");
            T.Eq(PetState.WaitingUser, S(m, 600), "toujours apres 10 min (WorkingTimeout ne s'applique pas)");
            m.Apply(E(K.ToolFinished, 601, detail: "q"));
            T.Eq(PetState.Working, S(m, 602), "reponse recue");
        });

        T.Case("AskedUser : un sous-agent de fond qui travaille ne leve pas l'attente", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.AskedUser, 1, tool: "AskUserQuestion", detail: "q"),
                E(K.SubagentActivity, 2, agent: "ag", detail: "fond"), E(K.ToolStarted, 3, agent: "ag", tool: "Grep", detail: "g"));
            T.Eq(PetState.WaitingUser, S(m, 4), "attente maintenue");
        });

        T.Case("NeedsUser => WaitingUser jusqu'a un evenement posterieur a la notification", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.NeedsUser, 2, detail: "permission_prompt"));
            T.Eq(PetState.WaitingUser, S(m, 3), "attente");
            // ligne ecrite avant la notification, lue apres : ne vaut pas reponse
            m.Apply(E(K.SessionActivity, 1.5));
            T.Eq(PetState.WaitingUser, S(m, 3), "evenement anterieur");
            m.Apply(E(K.ToolFinished, 10, detail: "b"));
            T.Eq(PetState.Working, S(m, 11), "outil autorise puis fini");
        });

        T.Case("NeedsUser : l'activite d'un autre fil ne leve pas l'attente du fil bloque", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Agent", detail: "ag"),
                E(K.SubagentActivity, 2, agent: "x", detail: "rapide"), E(K.SubagentActivity, 2, agent: "y", detail: "bloque"),
                E(K.ToolStarted, 3, agent: "x", tool: "Read", detail: "r"),
                E(K.ToolFinished, 4, agent: "x", detail: "r"),
                E(K.ToolStarted, 5, agent: "y", tool: "Bash", detail: "yb"),
                E(K.NeedsUser, 5.2, detail: "permission_prompt"),
                E(K.ToolStarted, 6, agent: "x", tool: "Grep", detail: "g"),
                E(K.ToolFinished, 7, agent: "x", detail: "g"));
            T.Eq(PetState.WaitingUser, S(m, 8), "le sous-agent x ne repond pas pour y");
            m.Apply(E(K.ToolFinished, 9, agent: "y", detail: "yb"));
            T.Eq(PetState.Working, S(m, 10), "y a eu sa reponse");
        });

        T.Case("NeedsUser sans outil en suspens : n'importe quel evenement posterieur la leve", () =>
        {
            var m = M(E(K.PromptSubmitted, -1), E(K.NeedsUser, 0, detail: "elicitation_dialog"));
            T.Eq(PetState.WaitingUser, S(m, 1), "attente");
            m.Apply(E(K.SessionActivity, 2));
            T.Eq(PetState.Working, S(m, 3), "levee");
        });

        T.Case("NeedsUser d'une session sans aucun evenement de transcript : ignore", () =>
        {
            // rien dans les transcripts ne pourrait lever cette attente
            var m = M(E(K.NeedsUser, 0, session: "inconnue", detail: "permission_prompt"));
            var s = m.Snapshot(At(1));
            T.Eq(PetState.Idle, s.State, "etat");
            T.Eq<string?>(null, s.MainSessionId, "principale");
            T.Eq(false, s.AnySessionActive, "active");
            T.Eq(0, s.Minis.Count, "minis");
            // la session apparait ensuite dans un transcript : l'ancienne demande ne revient pas
            m.Apply(E(K.PromptSubmitted, 2, session: "inconnue"));
            T.Eq(PetState.Working, S(m, 3), "travail, pas d'attente");
        });

        T.Case("NeedsUser expire apres NeedsUserTimeout (10 min) ; AskedUser garde WaitingTimeout (1 h)", () =>
        {
            // session tuee pendant l'attente, ou outil autorise qui tourne sans rien ecrire
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.NeedsUser, 2, detail: "permission_prompt"));
            T.Eq(PetState.WaitingUser, S(m, 601), "dans les 10 min");
            T.Eq(PetState.Idle, S(m, 603), "au-dela");
            var q = M(E(K.PromptSubmitted, 0), E(K.AskedUser, 2, tool: "AskUserQuestion", detail: "q"));
            T.Eq(PetState.WaitingUser, S(q, 603), "question : toujours en attente");
        });

        T.Case("question ou plan suivis de la notification permission_prompt du meme dialogue : le delai reste d'1 h", () =>
        {
            // d'apres le binaire de Claude Code 2.1.283 (deduction, non observee), ces dialogues
            // emettent aussi une Notification permission_prompt, environ 6 s apres le tool_use
            var src = new TranscriptSource(Jsonl.Session, null, null);
            foreach (var tool in new[] { "AskUserQuestion", "ExitPlanMode" })
            {
                var m = new ActivityModel();
                foreach (var line in new[] { Jsonl.Prompt(0), Jsonl.Assistant(1, "tool_use", Jsonl.ToolUse(tool, "q")) })
                    foreach (var e in TranscriptParser.ParseLine(line, src))
                    {
                        m.Apply(e);
                        m.Snapshot(e.Time);
                    }
                m.Apply(new ActivityEvent(At(7), K.NeedsUser, Jsonl.Session, null, Jsonl.Cwd, null, false, "permission_prompt"));
                T.Eq(PetState.WaitingUser, S(m, 700), tool + " : toujours en attente apres 10 min");
                T.Eq(PetState.WaitingUser, S(m, 3500), tool + " : a 3500 s");
                T.Eq(PetState.Idle, S(m, 3601.5), tool + " : fini 1 h apres la question, pas 1 h apres la notification");
            }
        });

        T.Case("ToolFinished en erreur => Error 2 s, puis Working", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.ToolFinished, 2, detail: "b", error: true));
            T.Eq(PetState.Error, S(m, 2.5), "erreur");
            T.Eq(PetState.Error, S(m, 3.9), "encore");
            T.Eq(PetState.Working, S(m, 4.1), "la session reste Working");
        });

        T.Case("ApiError => Error 2 s, la session reste Working", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ApiError, 1, error: true));
            T.Eq(PetState.Error, S(m, 2), "erreur");
            T.Eq(PetState.Working, S(m, 3.5), "reprise");
        });

        T.Case("system api_error date d'avant la fin du tour, lu apres elle : ne rouvre pas le tour", () =>
        {
            // la relecture des vrais transcripts en compte (ligne "api_error lus apres la fin du tour")
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.ToolFinished, 2, detail: "b"),
                E(K.TurnEnded, 3), E(K.ApiError, 2.5, error: true, detail: "api_error"));
            T.Eq(PetState.Celebrating, S(m, 4), "fete");
            T.Eq(PetState.Idle, S(m, 7), "puis repos, et non Working pendant 3 min");
            m.Apply(E(K.PromptSubmitted, 10));
            m.Apply(E(K.ApiError, 11, error: true, detail: "api_error"));
            T.Eq(PetState.Error, S(m, 11.5), "une erreur du tour suivant compte");
        });

        T.Case("TurnEnded apres un outil => Celebrating 3 s, puis inactive", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Edit", detail: "e"), E(K.ToolFinished, 2, detail: "e"), E(K.TurnEnded, 3));
            T.Eq(PetState.Celebrating, S(m, 3.1), "fete");
            T.Eq(PetState.Celebrating, S(m, 5.9), "encore");
            var after = m.Snapshot(At(6.1));
            T.Eq(PetState.Idle, after.State, "fini");
            T.Eq<string?>(null, after.MainSessionId, "plus de principale");
        });

        T.Case("TurnEnded d'un tour court sans outil : pas de fete", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.SessionActivity, 2), E(K.TurnEnded, 4));
            T.Eq(PetState.Idle, S(m, 4.1), "pas de fete");
        });

        T.Case("TurnEnded d'un tour sans outil mais d'au moins 10 s : fete", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.SessionActivity, 5), E(K.TurnEnded, 10));
            T.Eq(PetState.Celebrating, S(m, 10.5), "fete");
        });

        T.Case("fin interrompue, erreur d'API ou message fabrique : pas de fete", () =>
        {
            foreach (var reason in new[] { "interrupted", "api_error", "synthetic" })
            {
                var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.TurnEnded, 20, detail: reason));
                T.True(S(m, 20.5) != PetState.Celebrating, "pas de fete pour " + reason);
            }
        });

        T.Case("fin vue deux fois (thinking puis text du meme message) : une seule fete", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"), E(K.ToolFinished, 2, detail: "b"),
                E(K.TurnEnded, 3), E(K.TurnEnded, 3.01));
            T.Eq(PetState.Celebrating, S(m, 5), "fete en cours");
            T.Eq(PetState.Idle, S(m, 6.1), "finie a 3 s de la premiere fin");
        });

        T.Case("garde-fou : Working sans evenement depuis 3 min => inactive", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"));
            T.Eq(PetState.Working, S(m, 180), "encore dans les 3 min");
            T.Eq(PetState.Idle, S(m, 182), "au-dela");
        });

        T.Case("garde-fou : WaitingUser depuis plus d'1 h => inactive", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.AskedUser, 1, tool: "AskUserQuestion", detail: "q"));
            T.Eq(PetState.WaitingUser, S(m, 3600), "dans l'heure");
            T.Eq(PetState.Idle, S(m, 3602), "au-dela");
        });

        T.Case("garde-fou : session oubliee 2 h apres son dernier evenement", () =>
        {
            // attente reglee plus longue que l'oubli : seul l'oubli peut la faire disparaitre
            var m = new ActivityModel(new ActivityOptions { WaitingTimeout = TimeSpan.FromHours(5), NeedsUserTimeout = TimeSpan.FromHours(5) });
            m.Apply(E(K.PromptSubmitted, 0));
            m.Apply(E(K.NeedsUser, 10, detail: "permission_prompt"));
            T.Eq(PetState.WaitingUser, S(m, 7200), "encore la a 2 h moins 10 s");
            T.Eq(PetState.Idle, S(m, 7211), "oubliee");
            T.Eq(PetState.Idle, S(m, 7300), "et pas de retour");
        });

        T.Case("une session qui a un sous-agent actif reste Working apres sa fin de tour", () =>
        {
            // l'agent de fond a un outil en suspens : il vit jusqu'a SubagentTimeout
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Agent", detail: "a1"), E(K.ToolFinished, 2, detail: "a1"),
                E(K.SubagentActivity, 3, agent: "bg", detail: "fond"), E(K.ToolStarted, 3, agent: "bg", tool: "Bash", detail: "bgb"), E(K.TurnEnded, 4));
            T.Eq(PetState.Celebrating, S(m, 5), "fete d'abord (priorite)");
            T.Eq(PetState.Working, S(m, 8), "puis Working grace au sous-agent");
            T.Eq(PetState.Working, S(m, 600), "meme 10 min plus tard, tant que l'agent vit");
            m.Apply(E(K.SubagentEnded, 700, agent: "bg"));
            T.Eq(PetState.Idle, S(m, 701), "l'agent fini, la session s'arrete");
        });

        T.Case("sous-agent : mini avec libelle, couleur = agentId, disparait a SubagentEnded", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.SubagentActivity, 1, agent: "ag1", detail: "Explorer le code"));
            var mini = m.Snapshot(At(2)).Minis.Single();
            T.Eq("subagent", mini.Kind, "type");
            T.Eq("Explorer le code", mini.Label, "libelle");
            T.Eq("ag1", mini.ColorKey, "couleur");
            T.Eq(PetState.Working, mini.State, "etat");
            T.Eq(At(1), mini.Since, "depuis");
            m.Apply(E(K.SubagentEnded, 3, agent: "ag1"));
            T.Eq(0, m.Snapshot(At(4)).Minis.Count, "disparu");
        });

        T.Case("sous-agent : libelle par defaut, erreur transitoire sur le mini", () =>
        {
            var m = M(E(K.ToolStarted, 1, agent: "ag2", tool: "Bash", detail: "b"), E(K.ToolFinished, 2, agent: "ag2", detail: "b", error: true));
            var mini = m.Snapshot(At(2.5)).Minis.Single();
            T.Eq("sous-agent", mini.Label, "libelle par defaut");
            T.Eq(PetState.Error, mini.State, "erreur");
            T.Eq(PetState.Working, m.Snapshot(At(4.5)).Minis.Single().State, "puis Working");
            T.Eq(PetState.Working, S(m, 4.5), "le principal n'herite pas de l'erreur du sous-agent");
        });

        T.Case("sous-agent : fin sur le resultat de StructuredOutput", () =>
        {
            var m = M(E(K.SubagentActivity, 1, agent: "wf", detail: "Verifier"),
                E(K.ToolStarted, 2, agent: "wf", tool: "StructuredOutput", detail: "so"));
            T.Eq(1, m.Snapshot(At(3)).Minis.Count, "actif");
            m.Apply(E(K.ToolFinished, 4, agent: "wf", detail: "so"));
            T.Eq(0, m.Snapshot(At(5)).Minis.Count, "fini");
        });

        T.Case("sous-agent : StructuredOutput refuse (erreur) ne le termine pas", () =>
        {
            var m = M(E(K.ToolStarted, 2, agent: "wf", tool: "StructuredOutput", detail: "so"), E(K.ToolFinished, 4, agent: "wf", detail: "so", error: true));
            T.Eq(1, m.Snapshot(At(5)).Minis.Count, "toujours la");
        });

        T.Case("sous-agent avec un outil en suspens : muet depuis 30 min => disparait", () =>
        {
            var m = M(E(K.SubagentActivity, 0, agent: "lent", detail: "x"), E(K.ToolStarted, 0, agent: "lent", tool: "Bash", detail: "long"));
            T.Eq(1, m.Snapshot(At(1799)).Minis.Count, "encore la");
            T.Eq(0, m.Snapshot(At(1801)).Minis.Count, "disparu");
            T.Eq(PetState.Idle, S(m, 1801), "et la session avec");
        });

        T.Case("sous-agent sans outil en suspens : muet depuis 3 min => inactif (Claude Code ferme)", () =>
        {
            var m = M(E(K.PromptSubmitted, 0),
                E(K.SubagentActivity, 1, agent: "mort", detail: "x"), E(K.ToolStarted, 1, agent: "mort", tool: "Read", detail: "r"),
                E(K.ToolFinished, 2, agent: "mort", detail: "r"),
                E(K.SubagentActivity, 2, agent: "outil", detail: "y"), E(K.ToolStarted, 2, agent: "outil", tool: "Bash", detail: "b"));
            T.Eq("mort,outil", string.Join(",", m.Snapshot(At(180)).Minis.Select(x => x.Id).OrderBy(x => x)), "les deux dans les 3 min");
            T.Eq("outil", string.Join(",", m.Snapshot(At(183)).Minis.Select(x => x.Id)), "reste celui qui attend son outil");
            T.Eq(PetState.Working, S(m, 183), "la session travaille grace a lui");
            m.Apply(E(K.SubagentActivity, 200, agent: "mort", detail: "x"));
            T.Eq(2, m.Snapshot(At(201)).Minis.Count, "il reparle : il revient");
        });

        T.Case("sous-agent au premier plan : fini quand le fil principal recoit le tool_result de son toolUseId", () =>
        {
            ActivityEvent Fg(K kind, double at, string agent, string toolUseId, string? tool = null, string? detail = null) =>
                E(kind, at, agent: agent, tool: tool, detail: detail) with { ForegroundToolUseId = toolUseId };
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Agent", detail: "toolu_ag"),
                Fg(K.SubagentActivity, 2, "fg", "toolu_ag", detail: "premier plan"),
                Fg(K.ToolStarted, 3, "fg", "toolu_ag", tool: "Bash", detail: "fb"), Fg(K.ToolFinished, 4, "fg", "toolu_ag", detail: "fb"),
                // derniere ligne de l'agent sur un stop_reason null : aucune fin dans son transcript
                Fg(K.SubagentActivity, 5, "fg", "toolu_ag", detail: "premier plan"));
            T.Eq(1, m.Snapshot(At(6)).Minis.Count, "actif");
            m.Apply(E(K.ToolFinished, 6, detail: "autre"));
            T.Eq(1, m.Snapshot(At(6.5)).Minis.Count, "un autre resultat ne le termine pas");
            m.Apply(E(K.ToolFinished, 7, detail: "toolu_ag"));
            T.Eq(0, m.Snapshot(At(7.5)).Minis.Count, "fini par le resultat de l'outil Agent");
            m.Apply(Fg(K.SubagentActivity, 5.5, "fg", "toolu_ag", detail: "premier plan"));
            T.Eq(0, m.Snapshot(At(8)).Minis.Count, "une ligne anterieure lue apres ne le ressuscite pas");
        });

        T.Case("SubagentEnded (ou TurnEnded) d'un agent inconnu d'une session connue : cree fini", () =>
        {
            // amorcage : la task-notification du parent est appliquee avant les lignes de l'agent
            foreach (var end in new[] { K.SubagentEnded, K.TurnEnded })
            {
                var m = M(E(K.PromptSubmitted, 0), E(end, 10, agent: "bg", detail: "completed"),
                    E(K.SubagentActivity, 5, agent: "bg", detail: "fond"), E(K.ToolStarted, 6, agent: "bg", tool: "Bash", detail: "b"));
                T.Eq(0, m.Snapshot(At(11)).Minis.Count, end + " : pas de mini fantome");
                T.Eq(PetState.Idle, S(m, 200), end + " : la session s'arrete a son WorkingTimeout, pas 30 min plus tard");
                m.Apply(E(K.SubagentActivity, 20, agent: "bg", detail: "fond"));
                T.Eq(1, m.Snapshot(At(21)).Minis.Count, end + " : relance posterieure : revient");
            }
        });

        T.Case("sous-agent : une ligne anterieure a sa fin, lue apres, ne le ressuscite pas", () =>
        {
            var m = M(E(K.SubagentActivity, 1, agent: "bg", detail: "fond"), E(K.SubagentEnded, 10, agent: "bg", detail: "completed"),
                E(K.SubagentActivity, 9, agent: "bg", detail: "fond"));
            T.Eq(0, m.Snapshot(At(11)).Minis.Count, "reste fini");
            m.Apply(E(K.SubagentActivity, 20, agent: "bg", detail: "fond"));
            T.Eq(1, m.Snapshot(At(21)).Minis.Count, "relance par SendMessage : revient");
        });

        T.Case("SubagentEnded d'une tache inconnue (Bash de fond) : sans effet", () =>
        {
            var m = M(E(K.SubagentEnded, 1, agent: "bash1", detail: "completed"));
            var s = m.Snapshot(At(2));
            T.Eq(PetState.Idle, s.State, "rien");
            T.Eq(0, s.Minis.Count, "pas de mini");
        });

        T.Case("principale collante : une autre session qui demarre ne la vole pas", () =>
        {
            var m = M(E(K.PromptSubmitted, 0));
            T.Eq(A, m.Snapshot(At(1)).MainSessionId, "A");
            m.Apply(E(K.PromptSubmitted, 2, session: B, cwd: @"C:\work\projet-b"));
            var s = m.Snapshot(At(3));
            T.Eq(A, s.MainSessionId, "toujours A");
            var mini = s.Minis.Single();
            T.Eq("session", mini.Kind, "B en mini");
            T.Eq("projet-b", mini.Label, "libelle = dossier");
            T.Eq(@"C:\work\projet-b", mini.ColorKey, "couleur = cwd");
        });

        T.Case("principale collante : gardee pendant sa fete, puis la suivante active", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Bash", detail: "b"));
            T.Eq(A, m.Snapshot(At(1.5)).MainSessionId, "A d'abord");
            m.Apply(E(K.PromptSubmitted, 2, session: B));
            T.Eq(A, m.Snapshot(At(2.5)).MainSessionId, "B ne la vole pas");
            m.Apply(E(K.TurnEnded, 3));
            var during = m.Snapshot(At(4));
            T.Eq(A, during.MainSessionId, "A pendant sa fete");
            T.Eq(PetState.Celebrating, during.State, "fete");
            var after = m.Snapshot(At(6.5));
            T.Eq(B, after.MainSessionId, "puis B");
            T.Eq(PetState.Working, after.State, "B travaille");
        });

        T.Case("principale : une autre session qui attend l'utilisateur prend la place", () =>
        {
            var m = M(E(K.PromptSubmitted, 0));
            T.Eq(A, m.Snapshot(At(0.5)).MainSessionId, "A d'abord");
            m.Apply(E(K.PromptSubmitted, 1, session: B));
            T.Eq(A, m.Snapshot(At(2)).MainSessionId, "A reste");
            m.Apply(E(K.AskedUser, 3, session: B, tool: "AskUserQuestion", detail: "q"));
            var s = m.Snapshot(At(4));
            T.Eq(B, s.MainSessionId, "B attend : elle passe devant");
            T.Eq(PetState.WaitingUser, s.State, "etat");
            T.Eq(A, s.Minis.Single().Id, "A devient un mini");
        });

        T.Case("principale inactive : la plus recente des actives la remplace", () =>
        {
            var m = M(E(K.PromptSubmitted, 0));
            T.Eq(A, m.Snapshot(At(1)).MainSessionId, "A d'abord");
            m.Apply(E(K.PromptSubmitted, 50, session: B));
            m.Apply(E(K.PromptSubmitted, 100, session: "session-c"));
            T.Eq(A, m.Snapshot(At(170)).MainSessionId, "A encore active");
            // A muette depuis plus de 3 min : B et C encore actives, C la plus recente
            T.Eq("session-c", m.Snapshot(At(200)).MainSessionId, "C");
        });

        T.Case("principale en attente perimee (session tuee) : cede la place a la session active, sans clignotement", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.PromptSubmitted, 1, session: B), E(K.ToolStarted, 2, session: B, tool: "Bash", detail: "b"),
                E(K.NeedsUser, 3, session: B, detail: "permission_prompt"));
            T.Eq(B, m.Snapshot(At(3.5)).MainSessionId, "attente fraiche : B passe devant");
            var mains = new List<string?>();
            for (int t = 4; t <= 400; t++)
            {
                // A continue de travailler, B ne dira plus rien
                if (t % 10 == 0) m.Apply(E(K.ToolStarted, t, tool: "Read", detail: "r" + t));
                mains.Add(m.Snapshot(At(t)).MainSessionId);
            }
            T.Eq(B, mains[120 - 4], "B garde la place pendant FreshWaitWindow (2 min)");
            T.Eq(A, mains[130 - 4], "puis A, active depuis le debut de l'attente de B");
            int changes = mains.Zip(mains.Skip(1)).Count(p => p.First != p.Second);
            T.Eq(1, changes, "un seul changement de principale sur 400 s");
            var mini = m.Snapshot(At(400)).Minis.Single();
            T.Eq(B, mini.Id, "B reste en mini");
            T.Eq(PetState.WaitingUser, mini.State, "toujours en attente");
        });

        T.Case("attente perimee a l'amorcage : ne vole pas la place de la session active", () =>
        {
            // lot d'amorcage applique d'un coup, premier instantane apres : aucune principale connue
            var m = new ActivityModel();
            foreach (var e in new[] { E(K.PromptSubmitted, -5, session: B), E(K.AskedUser, 0, session: B, tool: "AskUserQuestion", detail: "q"),
                E(K.PromptSubmitted, 100), E(K.ToolStarted, 150, tool: "Bash", detail: "b") })
                m.Apply(e);
            var s = m.Snapshot(At(200));
            T.Eq(A, s.MainSessionId, "A, active apres le debut de l'attente de B");
            T.Eq(PetState.WaitingUser, s.Minis.Single(x => x.Id == B).State, "B en mini, au telephone");
        });

        T.Case("attente posterieure a la derniere activite de la principale : prend la place meme apres 2 min", () =>
        {
            var m = new ActivityModel();
            m.Apply(E(K.PromptSubmitted, 0));
            m.Apply(E(K.ToolStarted, 1, tool: "Bash", detail: "long"));
            T.Eq(A, m.Snapshot(At(1)).MainSessionId, "A d'abord");
            m.Apply(E(K.PromptSubmitted, 2, session: B));
            m.Apply(E(K.AskedUser, 5, session: B, tool: "AskUserQuestion", detail: "q"));
            T.Eq(B, m.Snapshot(At(150)).MainSessionId, "A muette depuis le debut de l'attente de B : B");

            var n = new ActivityModel();
            n.Apply(E(K.PromptSubmitted, 0));
            n.Apply(E(K.ToolStarted, 1, tool: "Bash", detail: "long"));
            T.Eq(A, n.Snapshot(At(1)).MainSessionId, "A d'abord");
            n.Apply(E(K.PromptSubmitted, 2, session: B));
            n.Apply(E(K.AskedUser, 5, session: B, tool: "AskUserQuestion", detail: "q"));
            n.Apply(E(K.ToolFinished, 100, detail: "long"));
            T.Eq(A, n.Snapshot(At(150)).MainSessionId, "A active apres le debut de l'attente, attente de B perimee : A reste");
        });

        T.Case("deux attentes qui vieillissent : un seul passage, jamais de va-et-vient", () =>
        {
            // chacune a un sous-agent de fond qui ecrit : sa derniere activite depasse l'attente de
            // l'autre. A perimee a 122 s cede a l'attente encore fraiche de B, puis plus rien ne bouge
            var m = M(E(K.PromptSubmitted, 0), E(K.AskedUser, 1, tool: "AskUserQuestion", detail: "qa"),
                E(K.PromptSubmitted, 2, session: B), E(K.AskedUser, 3, session: B, tool: "AskUserQuestion", detail: "qb"));
            var mains = new List<string?>();
            for (int t = 4; t <= 400; t++)
            {
                if (t % 7 == 0) m.Apply(E(K.SubagentActivity, t, agent: "fa", detail: "fond a"));
                if (t % 11 == 0) m.Apply(E(K.SubagentActivity, t, session: B, agent: "fb", detail: "fond b"));
                mains.Add(m.Snapshot(At(t)).MainSessionId);
            }
            T.Eq(A, mains[121 - 4], "A tant que son attente est fraiche");
            T.Eq(B, mains[122 - 4], "puis l'attente plus fraiche de B");
            int changes = mains.Zip(mains.Skip(1)).Count(p => p.First != p.Second);
            T.Eq(1, changes, "un seul changement sur 400 s");
        });

        T.Case("attente perimee : pas de successeur sur le point de s'eteindre (C, A, C en 0,5 s)", () =>
        {
            // A n'est plus active que par un sous-agent muet, sans outil en suspens, qui s'eteint a
            // 131,5 s ; sa fin de tour, a 20 s, suit le debut de l'attente de C. Sans marge, A prenait
            // la place a 131,5 s, quand l'attente de C devient perimee, et la rendait a C 0,5 s apres
            var m = M(E(K.SubagentActivity, -48.5, agent: "bg", detail: "fond"),
                E(K.PromptSubmitted, 10, session: C), E(K.AskedUser, 11, session: C, tool: "AskUserQuestion", detail: "q"),
                E(K.PromptSubmitted, 12), E(K.TurnEnded, 20));
            var mains = new List<string?>();
            for (double t = 11; t <= 400; t += 0.5) mains.Add(m.Snapshot(At(t)).MainSessionId);
            T.True(mains.All(x => x == C), "C garde la place : " + string.Join(" ", Runs(mains, 11)));
        });

        T.Case("attente perimee qui a cede la place : ne la reprend pas quand le successeur s'eteint", () =>
        {
            // A, active jusqu'a 200 s par son sous-agent, succede a C quand l'attente de C devient
            // perimee. A eteinte, C ne revient pas sans evenement nouveau : elle reste en mini
            var m = M(E(K.PromptSubmitted, 10, session: C), E(K.AskedUser, 11, session: C, tool: "AskUserQuestion", detail: "q"),
                E(K.SubagentActivity, 20, agent: "bg", detail: "fond"));
            var mains = new List<string?>();
            for (double t = 21; t <= 290; t += 0.5) mains.Add(m.Snapshot(At(t)).MainSessionId);
            T.Eq(C, mains[Index(131, 21)], "C tant que son attente est fraiche");
            T.Eq(A, mains[Index(131.5, 21)], "puis A, active depuis le debut de l'attente");
            T.Eq(A, mains[Index(200, 21)], "A jusqu'a son dernier instant");
            T.True(mains.Skip(Index(200.5, 21)).All(x => x == null), "puis plus de principale : " + string.Join(" ", Runs(mains, 21)));
            var s = m.Snapshot(At(290));
            T.Eq(PetState.WaitingUser, s.Minis.Single(x => x.Id == C).State, "C en mini, au telephone");
            // un evenement nouveau de C : elle redevient candidate
            m.Apply(E(K.ToolFinished, 300, session: C, detail: "q"));
            T.Eq(C, m.Snapshot(At(300.5)).MainSessionId, "C repond : elle travaille, principale");
        });

        T.Case("attente cedee puis nouvelle attente de la meme session : reprend la place", () =>
        {
            var m = M(E(K.PromptSubmitted, 10, session: C), E(K.NeedsUser, 11, session: C, detail: "permission_prompt"),
                E(K.SubagentActivity, 20, agent: "bg", detail: "fond"));
            for (double t = 21; t <= 250; t += 0.5) m.Snapshot(At(t));
            T.Eq<string?>(null, m.Snapshot(At(250)).MainSessionId, "C a cede, A eteinte");
            m.Apply(E(K.NeedsUser, 251, session: C, detail: "permission_prompt"));
            var s = m.Snapshot(At(251.5));
            T.Eq(C, s.MainSessionId, "nouvelle demande : C principale");
            T.Eq(PetState.WaitingUser, s.State, "au telephone");
        });

        T.Case("fuzz, 3 sessions au pas de 0,5 s : jamais de retour X, Y, X sans evenement nouveau", () =>
        {
            int seeds = 120, departures = 0, returns = 0, quick = 0, yielded = 0;
            long steps = 0;
            var failures = new List<string>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var (mains, touched) = Fuzz(seed, TimeSpan.FromHours(3));
                int n = mains.Count;
                steps += n;
                // par session : prochain pas ou elle est principale, pas ou elle attend en mini, pas ou
                // elle recoit un evenement (sommes cumulees)
                var next = new int[FuzzIds.Length][];
                var waitPre = new int[FuzzIds.Length][];
                var touchPre = new int[FuzzIds.Length][];
                var anyPre = new int[n + 1];
                for (int x = 0; x < FuzzIds.Length; x++)
                {
                    next[x] = new int[n + 1];
                    waitPre[x] = new int[n + 1];
                    touchPre[x] = new int[n + 1];
                    next[x][n] = n;
                    for (int j = n - 1; j >= 0; j--) next[x][j] = mains[j].Id == FuzzIds[x] ? j : next[x][j + 1];
                    for (int j = 0; j < n; j++)
                    {
                        waitPre[x][j + 1] = waitPre[x][j] + (mains[j].OthersWaiting(FuzzIds[x]) ? 1 : 0);
                        touchPre[x][j + 1] = touchPre[x][j] + ((touched[j] >> x) & 1);
                    }
                }
                for (int j = 0; j < n; j++) anyPre[j + 1] = anyPre[j] + (touched[j] != 0 ? 1 : 0);

                for (int i = 1; i < n; i++)
                {
                    if (mains[i - 1].Id is not { } id || mains[i].Id == id) continue;
                    departures++;
                    int x = Array.IndexOf(FuzzIds, id);
                    int k = next[x][i];
                    if (k == n) continue;
                    returns++;
                    string where = $"graine {seed}, {id} quitte la place a {i * 0.5:F1} s et la reprend a {k * 0.5:F1} s";
                    // un va-et-vient court sans aucun evenement : le choix se contredit d'un instantane a l'autre
                    if (k - i <= 20)
                    {
                        quick++;
                        if (anyPre[k + 1] - anyPre[i] == 0) failures.Add("va-et-vient sans evenement : " + where);
                    }
                    // une attente qui a cede la place, toujours la meme, ne la reprend pas sans rien dire de neuf
                    bool stillWaiting = waitPre[x][k] - waitPre[x][i] == k - i;
                    if (mains[i - 1].Waiting && stillWaiting && mains[k].Waiting)
                    {
                        yielded++;
                        if (touchPre[x][k + 1] - touchPre[x][i] == 0) failures.Add("attente cedee reprise sans evenement : " + where);
                    }
                }
            }
            Console.WriteLine($"      {seeds} graines, {steps} instantanes, {departures} changements de principale, {returns} retours " +
                              $"dont {quick} en moins de 10 s et {yielded} d'une attente cedee ; injustifies : " +
                              $"{failures.Count(x => x.StartsWith("va-et-vient"))} va-et-vient, {failures.Count(x => x.StartsWith("attente"))} attentes cedees reprises");
            T.True(failures.Count == 0, failures.Count + " retours injustifies, dont : " + string.Join(" ; ", failures.Take(3)));
            T.True(departures > 1000 && returns > 100, "le fuzz fait bouger la principale");
        });

        T.Case("minis : une conversation en attente passe devant des sous-agents plus recents, jamais coupee", () =>
        {
            // B active depuis longtemps : par Since, elle serait la derniere et sortirait de l'instantane
            var m = M(E(K.PromptSubmitted, -100, session: B, cwd: @"C:\work\projet-b"),
                E(K.PromptSubmitted, -50, session: C), E(K.PromptSubmitted, 0), E(K.AskedUser, 1, tool: "AskUserQuestion", detail: "qa"),
                E(K.AskedUser, 3, session: C, tool: "AskUserQuestion", detail: "qc"),
                E(K.AskedUser, 5, session: B, tool: "AskUserQuestion", detail: "qb"));
            for (int i = 1; i <= 8; i++) m.Apply(E(K.SubagentActivity, 10 + i, agent: "ag" + i, detail: "n" + i));
            var s = m.Snapshot(At(20));
            T.Eq(A, s.MainSessionId, "A principale");
            T.Eq(PetState.WaitingUser, s.State, "A attend");
            T.Eq(6, s.Minis.Count, "plafond");
            T.Eq(4, s.MinisOverflow, "debordement");
            T.Eq(B + "," + C + ",ag8,ag7,ag6,ag5", string.Join(",", s.Minis.Select(x => x.Id)), "attentes d'abord, la plus recente en tete, puis les plus recents");
            T.True(s.Minis.Take(2).All(x => x.State == PetState.WaitingUser), "au telephone");
        });

        T.Case("minis : plafond de 6, les plus recents d'abord, le reste en debordement", () =>
        {
            var m = M(E(K.PromptSubmitted, 0));
            for (int i = 1; i <= 8; i++) m.Apply(E(K.SubagentActivity, i, agent: "ag" + i, detail: "n" + i));
            var s = m.Snapshot(At(10));
            T.Eq(6, s.Minis.Count, "plafond");
            T.Eq(2, s.MinisOverflow, "debordement");
            T.Eq("ag8", s.Minis[0].Id, "le plus recent en tete");
            T.Eq("ag3", s.Minis[5].Id, "le sixieme");
        });

        T.Case("minis : sous-agents de toutes les sessions + sessions non principales", () =>
        {
            var m = M(E(K.PromptSubmitted, 0));
            m.Snapshot(At(0.5));
            foreach (var e in new[] { E(K.SubagentActivity, 1, agent: "a-de-A"),
                E(K.PromptSubmitted, 2, session: B, cwd: @"C:\work\b"), E(K.SubagentActivity, 3, session: B, agent: "a-de-B") })
                m.Apply(e);
            var s = m.Snapshot(At(4));
            T.Eq(A, s.MainSessionId, "principale");
            T.Eq("a-de-B,session-b,a-de-A", string.Join(",", s.Minis.Select(x => x.Id)), "minis");
            T.Eq(true, s.AnySessionActive, "active");
        });

        T.Case("enchainement reel : lignes synthetiques parsees puis appliquees", () =>
        {
            var src = new TranscriptSource(Jsonl.Session, null, null);
            var m = new ActivityModel();
            var lines = new[]
            {
                Jsonl.Prompt(0),
                Jsonl.Assistant(1, "tool_use", Jsonl.Thinking()),
                Jsonl.Assistant(1.1, "tool_use", Jsonl.ToolUse("Bash", "b1")),
                Jsonl.Result(3, "b1"),
                Jsonl.Assistant(4, "end_turn", Jsonl.Thinking()),
                Jsonl.Assistant(4.1, "end_turn", Jsonl.Text("fini")),
                Jsonl.SystemLine(4.2, "stop_hook_summary"),
            };
            var seen = new List<PetState>();
            foreach (var line in lines)
                foreach (var e in TranscriptParser.ParseLine(line, src))
                {
                    m.Apply(e);
                    seen.Add(m.Snapshot(e.Time).State);
                }
            T.True(seen.Contains(PetState.Working), "a travaille");
            T.Eq(PetState.Celebrating, m.Snapshot(At(5)).State, "fete");
            T.Eq("demo-projet", m.Snapshot(At(5)).MainLabel, "libelle");
            T.Eq(PetState.Idle, m.Snapshot(At(8)).State, "puis repos");
        });
    }

    /// <summary>Principale en suites "id@heure", pour les messages d'echec.</summary>
    static IEnumerable<string> Runs(List<string?> mains, double start)
    {
        for (int i = 0; i < mains.Count; i++)
            if (i == 0 || mains[i] != mains[i - 1]) yield return $"{mains[i] ?? "-"}@{start + i * 0.5:F1}";
    }

    static int Index(double t, double start) => (int)Math.Round((t - start) / 0.5);

    // ---- fuzz de la principale ----------------------------------------------------

    static readonly string[] FuzzIds = { A, B, C };

    /// <summary>Principale d'un instantane, et les sessions qui attendent en mini (masque sur FuzzIds).</summary>
    readonly record struct MainAt(string? Id, bool Waiting, int MinisWaiting)
    {
        public bool OthersWaiting(string id) => (MinisWaiting & (1 << Array.IndexOf(FuzzIds, id))) != 0;
    }

    /// <summary>
    /// Trois sessions qui vivent au hasard : tours, outils, questions et plans, demandes du hook,
    /// sous-agents de fond, erreurs, fins, et des silences de quelques secondes a plus de deux
    /// heures, qui font jouer tous les delais. Instantane tous les 0,5 s, comme l'app. Rend la
    /// principale de chaque pas, et le masque des sessions qui ont recu un evenement a ce pas.
    /// </summary>
    static (List<MainAt> Mains, List<int> Touched) Fuzz(int seed, TimeSpan duration)
    {
        var rng = new Random(seed);
        var model = new ActivityModel();
        var sessions = FuzzIds.Select(id => new FuzzSession(id) { Next = rng.NextDouble() * 120 }).ToArray();
        var mains = new List<MainAt>();
        var touched = new List<int>();
        for (int step = 0; step * 0.5 <= duration.TotalSeconds; step++)
        {
            double t = step * 0.5;
            int hit = 0;
            for (int i = 0; i < sessions.Length; i++)
            {
                var f = sessions[i];
                while (f.Next <= t)
                {
                    foreach (var e in f.Act(rng)) model.Apply(e);
                    hit |= 1 << i;
                    f.Next += FuzzDelay(rng);
                }
            }
            var s = model.Snapshot(At(t));
            int waiting = 0;
            foreach (var mini in s.Minis)
                if (mini.Kind == "session" && mini.State == PetState.WaitingUser) waiting |= 1 << Array.IndexOf(FuzzIds, mini.Id);
            mains.Add(new MainAt(s.MainSessionId, s.State == PetState.WaitingUser, waiting));
            touched.Add(hit);
        }
        return (mains, touched);
    }

    static double FuzzDelay(Random r) => r.NextDouble() switch
    {
        < 0.55 => 0.3 + r.NextDouble() * 10,
        < 0.80 => 10 + r.NextDouble() * 110,
        < 0.94 => 120 + r.NextDouble() * 480,
        < 0.99 => 600 + r.NextDouble() * 2400,
        _ => 3000 + r.NextDouble() * 6000,
    };

    /// <summary>Une session du fuzz : son tour, ses outils en suspens, sa question, ses sous-agents.</summary>
    sealed class FuzzSession(string id)
    {
        public double Next;
        readonly string _id = id;
        bool _turn;
        string? _asked;
        int _serial;
        readonly List<string> _tools = new();
        readonly Dictionary<string, List<string>> _agents = new();
        List<ActivityEvent> _out = new();

        void Add(K kind, string? agent = null, string? tool = null, string? detail = null, bool error = false) =>
            _out.Add(E(kind, Next, session: _id, agent: agent, tool: tool, detail: detail, error: error, cwd: @"C:\work\" + _id));

        string NewId() => _id + "-" + ++_serial;

        public List<ActivityEvent> Act(Random r)
        {
            _out = new List<ActivityEvent>();
            double x = r.NextDouble();
            if (!_turn)
            {
                if (x < 0.65) { _turn = true; Add(K.PromptSubmitted); }
                else if (x < 0.9) Agent(r);
                else Add(K.NeedsUser, detail: "permission_prompt");
                return _out;
            }
            if (_asked != null && x < 0.4)
            {
                // reponse a la question ou au plan
                _tools.Remove(_asked);
                Add(K.ToolFinished, detail: _asked);
                _asked = null;
                return _out;
            }
            x = r.NextDouble();
            if (x < 0.22)
            {
                var t = NewId();
                _tools.Add(t);
                Add(K.ToolStarted, tool: "Bash", detail: t);
            }
            else if (x < 0.36)
            {
                if (_tools.Count == 0) { Add(K.SessionActivity); return _out; }
                var t = _tools[r.Next(_tools.Count)];
                _tools.Remove(t);
                if (t == _asked) _asked = null;
                Add(K.ToolFinished, detail: t, error: r.Next(8) == 0);
            }
            else if (x < 0.44)
            {
                var t = NewId();
                _tools.Add(t);
                _asked = t;
                Add(K.AskedUser, tool: r.Next(2) == 0 ? "AskUserQuestion" : "ExitPlanMode", detail: t);
            }
            else if (x < 0.52) Add(K.NeedsUser, detail: r.Next(4) == 0 ? "elicitation_dialog" : "permission_prompt");
            else if (x < 0.74) Agent(r);
            else if (x < 0.77) Add(K.ApiError, error: true, detail: "api_error");
            else if (x < 0.90)
            {
                _turn = false;
                _tools.Clear();
                _asked = null;
                Add(K.TurnEnded, detail: r.Next(5) == 0 ? "interrupted" : null);
            }
            else if (x < 0.93) Add(K.PromptSubmitted);
            else Add(K.SessionActivity);
            return _out;
        }

        /// <summary>Un pas d'un des deux sous-agents de fond de la session.</summary>
        void Agent(Random r)
        {
            string agent = _id + "-ag" + r.Next(2);
            double y = r.NextDouble();
            if (!_agents.TryGetValue(agent, out var tools) || y < 0.3)
            {
                _agents[agent] = new List<string>();
                Add(K.SubagentActivity, agent: agent, detail: "fond");
            }
            else if (y < 0.55)
            {
                var t = NewId();
                tools.Add(t);
                Add(K.ToolStarted, agent: agent, tool: "Bash", detail: t);
            }
            else if (y < 0.8 && tools.Count > 0)
            {
                var t = tools[0];
                tools.RemoveAt(0);
                Add(K.ToolFinished, agent: agent, detail: t);
            }
            else if (y < 0.9)
            {
                _agents.Remove(agent);
                Add(K.SubagentEnded, agent: agent, detail: "completed");
            }
            else Add(K.SubagentActivity, agent: agent, detail: "fond");
        }
    }
}
