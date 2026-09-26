using System.Diagnostics;
using System.Text;
using static Hamster.Activity.Tests.Jsonl;

namespace Hamster.Activity.Tests;

static class WatcherTests
{
    const string SessionB = "22222222-3333-4444-5555-666666666666";

    static string SessionFile(string root, string session = Session)
    {
        string slug = Path.Combine(root, "C--work-demo");
        Directory.CreateDirectory(slug);
        return Path.Combine(slug, session + ".jsonl");
    }

    static void Append(string path, string line) => File.AppendAllText(path, line + "\n");

    static string ToolLine(double at, string id) => Assistant(at, "tool_use", ToolUse("Bash", id));

    static bool IsTool(ActivityEvent e, string id) => e.Kind == ActivityKind.ToolStarted && e.Detail == id;

    static (TranscriptWatcher, Collector) Start(string root, Action<TranscriptWatcher>? tune = null)
    {
        var w = new TranscriptWatcher(root);
        tune?.Invoke(w);
        var c = new Collector();
        w.Events += c.Add;
        w.Start();
        return (w, c);
    }

    public static void Run()
    {
        T.Suite("TranscriptWatcher");

        T.Case("historique saute : un fichier ancien ne rejoue rien, ses ajouts si", () =>
        {
            string root = TempDir.Create("hist");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, ToolLine(0, "old1") + "\n" + ToolLine(1, "old2") + "\n");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(400);
                    T.Eq(0, c.Count, "rien au demarrage");
                    Append(file, ToolLine(2, "new1"));
                    T.True(c.WaitFor(e => IsTool(e, "new1"), TimeSpan.FromSeconds(3)) != null, "ajout recu");
                    T.True(!c.All().Any(e => e.Detail is "old1" or "old2"), "historique jamais relu");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("amorcage : fin (64 Ko) d'un fichier recent relue, sans ligne coupee", () =>
        {
            string root = TempDir.Create("prime");
            try
            {
                string file = SessionFile(root);
                var sb = new StringBuilder();
                int n = 0;
                while (sb.Length < 200 * 1024) sb.Append(ToolLine(n, "p" + n++)).Append('\n');
                File.WriteAllText(file, sb.ToString());
                var (w, c) = Start(root);
                using (w)
                {
                    T.True(c.WaitFor(e => IsTool(e, "p" + (n - 1)), TimeSpan.FromSeconds(3)) != null, "derniere ligne relue");
                    Thread.Sleep(200);
                    var ids = c.All().Where(e => e.Kind == ActivityKind.ToolStarted).Select(e => int.Parse(e.Detail![1..])).ToList();
                    int lineLength = ToolLine(0, "p1000").Length + 1;
                    T.True(!ids.Contains(0), "le debut du fichier n'est pas relu");
                    T.True(ids.Count >= 64 * 1024 / lineLength - 2 && ids.Count <= 64 * 1024 / lineLength + 2, $"environ 64 Ko relus ({ids.Count} lignes)");
                    T.Eq(ids.Count, ids.Distinct().Count(), "pas de doublon");
                    T.True(ids.SequenceEqual(Enumerable.Range(ids[0], ids.Count)), "suite continue jusqu'a la fin");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("latence : ajout lu en moins de 200 ms (mediane de 10 ajouts)", () =>
        {
            string root = TempDir.Create("lat");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    var latencies = new List<double>();
                    for (int i = 0; i < 10; i++)
                    {
                        long before = Stopwatch.GetTimestamp();
                        Append(file, ToolLine(i, "lat" + i));
                        var got = c.WaitFor(e => IsTool(e, "lat" + i), TimeSpan.FromSeconds(3));
                        T.True(got != null, "ajout " + i + " jamais recu");
                        latencies.Add(Collector.Ms(before, got!.Value));
                        Thread.Sleep(120);
                    }
                    latencies.Sort();
                    double median = latencies[latencies.Count / 2];
                    Console.WriteLine($"      latence : min {latencies[0]:F0} ms, mediane {median:F0} ms, max {latencies[^1]:F0} ms");
                    T.True(median < 200, $"mediane {median:F0} ms");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("ecrivain qui garde son handle ouvert : lignes recues quand meme", () =>
        {
            string root = TempDir.Create("open");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root, x => x.RescanInterval = TimeSpan.FromSeconds(1));
                using (w)
                using (var writer = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    Thread.Sleep(300);
                    long before = Stopwatch.GetTimestamp();
                    var bytes = Encoding.UTF8.GetBytes(ToolLine(1, "held") + "\n");
                    writer.Write(bytes);
                    writer.Flush();
                    var got = c.WaitFor(e => IsTool(e, "held"), TimeSpan.FromSeconds(4));
                    T.True(got != null, "jamais recu");
                    Console.WriteLine($"      latence handle ouvert : {Collector.Ms(before, got!.Value):F0} ms");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("ligne partielle gardee en tampon jusqu'a son saut de ligne", () =>
        {
            string root = TempDir.Create("partial");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    string line = ToolLine(1, "half");
                    File.AppendAllText(file, line[..(line.Length / 2)]);
                    Thread.Sleep(400);
                    T.Eq(0, c.Count, "rien pour une demi-ligne");
                    File.AppendAllText(file, line[(line.Length / 2)..] + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "half"), TimeSpan.FromSeconds(3)) != null, "ligne complete recue");
                    T.Eq(1, c.All().Count(e => e.Kind == ActivityKind.ToolStarted), "une seule fois");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("troncature : l'offset repart a zero", () =>
        {
            string root = TempDir.Create("trunc");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    Append(file, ToolLine(1, "before1"));
                    Append(file, ToolLine(2, "before2"));
                    T.True(c.WaitFor(e => IsTool(e, "before2"), TimeSpan.FromSeconds(3)) != null, "avant");
                    File.WriteAllText(file, ToolLine(3, "after") + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "after"), TimeSpan.FromSeconds(3)) != null, "apres troncature");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("sous-agent de workflow lu avec son libelle, journal.jsonl ignore", () =>
        {
            string root = TempDir.Create("agents");
            try
            {
                string wf = Path.Combine(root, "C--work-demo", Session, "subagents", "workflows", "wf_1");
                Directory.CreateDirectory(wf);
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    File.WriteAllText(Path.Combine(wf, "agent-a1b2.meta.json"), "{\"agentType\":\"workflow-subagent\",\"description\":\"Verifier le build\"}");
                    File.AppendAllText(Path.Combine(wf, "journal.jsonl"), Prompt(1) + "\n");
                    File.AppendAllText(Path.Combine(wf, "agent-a1b2.jsonl"), AssistantAs("a1b2", 2, null, ToolUse("Read", "sub1")) + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "sub1"), TimeSpan.FromSeconds(3)) != null, "agent lu");
                    Thread.Sleep(200);
                    var all = c.All();
                    T.Eq("a1b2", all.First(e => IsTool(e, "sub1")).AgentId, "agentId");
                    T.Eq("Verifier le build", all.First(e => e.Kind == ActivityKind.SubagentActivity).Detail, "libelle");
                    T.Eq(Session, all.First(e => IsTool(e, "sub1")).SessionId, "session parente");
                    T.True(!all.Any(e => e.Kind == ActivityKind.PromptSubmitted), "journal ignore");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("amorcage : lot trie par heure (tri stable), la fin annoncee par le parent n'est plus perdue", () =>
        {
            string root = TempDir.Create("prime-order");
            try
            {
                // l'enumeration rend le transcript du parent avant ceux des sous-agents
                string file = SessionFile(root);
                File.WriteAllText(file, Prompt(0) + "\n" + Assistant(0.5, "tool_use", ToolUse("Agent", "toolu_bg")) + "\n"
                    + Result(0.6, "toolu_bg") + "\n" + TaskNotification(10, "bgagent", "completed") + "\n");
                string sub = Path.Combine(root, "C--work-demo", Session, "subagents");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "agent-bgagent.meta.json"), "{\"agentType\":\"general-purpose\",\"description\":\"Fond\",\"requestShape\":\"background\",\"toolUseId\":\"toolu_bg\"}");
                File.WriteAllText(Path.Combine(sub, "agent-bgagent.jsonl"), UserString(1, "mission", agentId: "bgagent") + "\n"
                    + AssistantAs("bgagent", 2, "tool_use", ToolUse("Bash", "bgb")) + "\n" + Result(3, "bgb", agentId: "bgagent") + "\n"
                    // derniere ligne sur un stop_reason null : seule la task-notification dit la fin
                    + AssistantAs("bgagent", 9, null, Text("rapport")) + "\n");
                var (w, c) = Start(root);
                using (w)
                {
                    T.True(c.WaitFor(e => e.Kind == ActivityKind.SubagentEnded, TimeSpan.FromSeconds(3)) != null, "lot d'amorcage recu");
                    Thread.Sleep(200);
                    T.Eq(1, c.Batches, "un seul lot");
                    var all = c.All();
                    T.True(all.Zip(all.Skip(1)).All(p => p.First.Time <= p.Second.Time), "heures croissantes");
                    int end = all.FindIndex(e => e.Kind == ActivityKind.SubagentEnded);
                    T.Eq(ActivityKind.PromptSubmitted, all[end + 1].Kind, "tri stable : la reprise suit la fin, comme dans la ligne");
                    T.Eq("task-notification", all[end + 1].Detail, "reprise de la task-notification");

                    var m = new ActivityModel();
                    foreach (var e in all) m.Apply(e);
                    T.Eq(0, m.Snapshot(T0.AddSeconds(11)).Minis.Count, "pas de mini fantome");
                    T.Eq(PetState.Idle, m.Snapshot(T0.AddSeconds(200)).State, "pas de Working jusqu'a 30 min");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("sous-agent au premier plan : fini par le tool_result du parent (requestShape du meta.json)", () =>
        {
            string root = TempDir.Create("foreground");
            try
            {
                string sub = Path.Combine(root, "C--work-demo", Session, "subagents");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "agent-fg.meta.json"), "{\"agentType\":\"Explore\",\"description\":\"Premier plan\",\"requestShape\":\"foreground\",\"toolUseId\":\"toolu_fg\"}");
                // fond : le resultat de l'outil Agent revient des le lancement, il ne dit pas la fin
                File.WriteAllText(Path.Combine(sub, "agent-bg.meta.json"), "{\"agentType\":\"Explore\",\"description\":\"Fond\",\"requestShape\":\"background\",\"toolUseId\":\"toolu_bgl\"}");
                // sans requestShape (versions plus anciennes) : pas de fin par le tool_result non plus
                File.WriteAllText(Path.Combine(sub, "agent-old.meta.json"), "{\"agentType\":\"Explore\",\"description\":\"Ancien\",\"toolUseId\":\"toolu_old\"}");
                foreach (var id in new[] { "fg", "bg", "old" })
                    File.WriteAllText(Path.Combine(sub, "agent-" + id + ".jsonl"), UserString(2, "mission", agentId: id) + "\n"
                        + AssistantAs(id, 3, "tool_use", ToolUse("Read", id + "r")) + "\n" + Result(4, id + "r", agentId: id) + "\n"
                        + AssistantAs(id, 5, null, Text("rapport")) + "\n");
                string file = SessionFile(root);
                File.WriteAllText(file, Prompt(0) + "\n"
                    + Assistant(1, "tool_use", ToolUse("Agent", "toolu_fg"), ToolUse("Agent", "toolu_bgl"), ToolUse("Agent", "toolu_old")) + "\n"
                    + Result(1.5, "toolu_bgl") + "\n" + Result(1.6, "toolu_old") + "\n" + Result(6, "toolu_fg") + "\n");
                var (w, c) = Start(root);
                using (w)
                {
                    T.True(c.WaitFor(e => e.Kind == ActivityKind.ToolFinished && e.Detail == "toolu_fg", TimeSpan.FromSeconds(3)) != null, "lot recu");
                    Thread.Sleep(200);
                    var m = new ActivityModel();
                    foreach (var e in c.All()) m.Apply(e);
                    var ids = m.Snapshot(T0.AddSeconds(7)).Minis.Select(x => x.Id).OrderBy(x => x);
                    T.Eq("bg,old", string.Join(",", ids), "l'agent au premier plan est fini, les autres non");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("aucun verrou garde : l'ecrivain peut ouvrir en exclusif, renommer, effacer", () =>
        {
            string root = TempDir.Create("lock");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    Append(file, ToolLine(1, "l1"));
                    T.True(c.WaitFor(e => IsTool(e, "l1"), TimeSpan.FromSeconds(3)) != null, "lu");
                    Thread.Sleep(100);
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    string moved = SessionFile(root, SessionB);
                    File.Move(file, moved);
                    File.Delete(moved);
                    Thread.Sleep(300);
                    T.True(w.Status.StartsWith("actif"), "statut : " + w.Status);
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("fichier recree apres suppression : relu depuis le debut", () =>
        {
            string root = TempDir.Create("recreate");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    Append(file, ToolLine(1, "first") + "\n" + ToolLine(1.5, "pad"));
                    T.True(c.WaitFor(e => IsTool(e, "pad"), TimeSpan.FromSeconds(3)) != null, "premier fichier");
                    File.Delete(file);
                    Thread.Sleep(300);
                    // plus long que l'ancien offset : sans la suppression, le debut serait perdu
                    File.WriteAllText(file, ToolLine(2, "second") + "\n" + ToolLine(3, "third") + "\n" + ToolLine(4, "fourth") + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "second"), TimeSpan.FromSeconds(3)) != null, "debut du nouveau fichier");
                    T.True(c.WaitFor(e => IsTool(e, "fourth"), TimeSpan.FromSeconds(3)) != null, "fin du nouveau fichier");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("lot dedoublonne : deux lignes identiques => un evenement", () =>
        {
            string root = TempDir.Create("dedup");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                using (w)
                {
                    Thread.Sleep(300);
                    string line = ToolLine(1, "dup");
                    File.AppendAllText(file, line + "\n" + line + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "dup"), TimeSpan.FromSeconds(3)) != null, "recu");
                    Thread.Sleep(200);
                    T.Eq(1, c.All().Count(e => IsTool(e, "dup")), "une fois");
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("dossier absent au demarrage : inactif, puis suivi quand il apparait", () =>
        {
            string parent = TempDir.Create("late");
            string root = Path.Combine(parent, "projects");
            try
            {
                var (w, c) = Start(root, x => x.RescanInterval = TimeSpan.FromMilliseconds(300));
                using (w)
                {
                    Thread.Sleep(200);
                    T.True(w.Status.StartsWith("inactif"), "statut : " + w.Status);
                    string file = SessionFile(root);
                    File.WriteAllText(file, ToolLine(1, "late1") + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "late1"), TimeSpan.FromSeconds(3)) != null, "fichier cree apres coup, lu depuis le debut");
                    Append(file, ToolLine(2, "late2"));
                    T.True(c.WaitFor(e => IsTool(e, "late2"), TimeSpan.FromSeconds(3)) != null, "puis suivi");
                    T.True(w.Status.StartsWith("actif"), "statut : " + w.Status);
                }
            }
            finally { TempDir.Delete(parent); }
        });

        T.Case("filet de securite seul (sans FileSystemWatcher) : nouveaux fichiers et ajouts rattrapes", () =>
        {
            string root = TempDir.Create("safety");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root, x =>
                {
                    x.UseFileSystemWatcher = false;
                    x.RescanInterval = TimeSpan.FromMilliseconds(200);
                    x.FullRescanInterval = TimeSpan.FromMilliseconds(600);
                });
                using (w)
                {
                    Thread.Sleep(300);
                    Append(file, ToolLine(1, "hot1"));
                    T.True(c.WaitFor(e => IsTool(e, "hot1"), TimeSpan.FromSeconds(2)) != null, "ajout vu par le controle leger");
                    string other = SessionFile(root, SessionB);
                    File.WriteAllText(other, ToolLine(2, "new1") + "\n");
                    T.True(c.WaitFor(e => IsTool(e, "new1"), TimeSpan.FromSeconds(3)) != null, "nouveau fichier vu par l'enumeration complete");
                    T.True(w.Status.Contains("rattrapages"), "statut : " + w.Status);
                }
            }
            finally { TempDir.Delete(root); }
        });

        T.Case("Dispose : le thread s'arrete, plus aucun lot", () =>
        {
            string root = TempDir.Create("dispose");
            try
            {
                string file = SessionFile(root);
                File.WriteAllText(file, "");
                var (w, c) = Start(root);
                Thread.Sleep(300);
                w.Dispose();
                Append(file, ToolLine(1, "after-dispose"));
                Thread.Sleep(400);
                T.Eq(0, c.Count, "rien apres Dispose");
                T.Eq("arrete", w.Status, "statut");
            }
            finally { TempDir.Delete(root); }
        });
    }
}
