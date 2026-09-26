using K = Hamster.Activity.ActivityKind;

namespace Hamster.Activity.Tests;

static class ModelTests
{
    static readonly DateTimeOffset T0 = Jsonl.T0;
    const string A = "session-a", B = "session-b";

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
            var m = M(E(K.NeedsUser, 0, detail: "elicitation_dialog"));
            T.Eq(PetState.WaitingUser, S(m, 1), "attente, meme sans transcript");
            m.Apply(E(K.SessionActivity, 2));
            T.Eq(PetState.Working, S(m, 3), "levee");
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
            var m = new ActivityModel(new ActivityOptions { WaitingTimeout = TimeSpan.FromHours(5) });
            m.Apply(E(K.PromptSubmitted, 0));
            m.Apply(E(K.NeedsUser, 10, detail: "permission_prompt"));
            T.Eq(PetState.WaitingUser, S(m, 7200), "encore la a 2 h moins 10 s");
            T.Eq(PetState.Idle, S(m, 7211), "oubliee");
            T.Eq(PetState.Idle, S(m, 7300), "et pas de retour");
        });

        T.Case("une session qui a un sous-agent actif reste Working apres sa fin de tour", () =>
        {
            var m = M(E(K.PromptSubmitted, 0), E(K.ToolStarted, 1, tool: "Agent", detail: "a1"), E(K.ToolFinished, 2, detail: "a1"),
                E(K.SubagentActivity, 3, agent: "bg", detail: "fond"), E(K.TurnEnded, 4));
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

        T.Case("sous-agent : muet depuis 30 min => disparait", () =>
        {
            var m = M(E(K.SubagentActivity, 0, agent: "lent", detail: "x"));
            T.Eq(1, m.Snapshot(At(1799)).Minis.Count, "encore la");
            T.Eq(0, m.Snapshot(At(1801)).Minis.Count, "disparu");
            T.Eq(PetState.Idle, S(m, 1801), "et la session avec");
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
}
