using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hamster.Activity.Tests;

static class HookTests
{
    static string Payload(string type, string session = "sess-1", bool indented = false, string? agentId = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["session_id"] = session,
            ["transcript_path"] = @"C:\x\" + session + ".jsonl",
            ["cwd"] = @"C:\work\demo",
            ["hook_event_name"] = "Notification",
            ["message"] = "Claude a besoin de vous",
            ["notification_type"] = type,
        };
        if (agentId != null) d["agent_id"] = agentId;
        return JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = indented });
    }

    static List<ActivityEvent> Parse(HookEventsWatcher w, string data, out int consumed)
    {
        var batch = new List<ActivityEvent>();
        consumed = w.ParseValues(Encoding.UTF8.GetBytes(data), batch);
        return batch;
    }

    static (HookEventsWatcher, Collector) Start(string path, Action<HookEventsWatcher>? tune = null)
    {
        var w = new HookEventsWatcher(path);
        tune?.Invoke(w);
        var c = new Collector();
        w.Events += c.Add;
        w.Start();
        return (w, c);
    }

    static bool Needs(ActivityEvent e, string session) => e.Kind == ActivityKind.NeedsUser && e.SessionId == session;

    public static void Run()
    {
        T.Suite("HookEventsWatcher");

        T.Case("notification_type retenus => NeedsUser, le reste ignore (idle_prompt compris)", () =>
        {
            var now = Jsonl.T0;
            foreach (var type in new[] { "permission_prompt", "agent_needs_input", "elicitation_dialog", "elicitation_url_dialog" })
            {
                using var doc = JsonDocument.Parse(Payload(type));
                var e = HookEventsWatcher.ToEvent(doc.RootElement, now);
                T.True(e != null, type);
                T.Eq(ActivityKind.NeedsUser, e!.Kind, "type");
                T.Eq(type, e.Detail, "detail");
                T.Eq("sess-1", e.SessionId, "session");
                T.Eq(@"C:\work\demo", e.Cwd, "cwd");
                T.Eq(now, e.Time, "heure de lecture");
                T.Eq<string?>(null, e.AgentId, "pas d'agent");
            }
            foreach (var type in new[] { "idle_prompt", "auth_success", "agent_completed", "elicitation_complete", "quota_auto_resume_fired" })
            {
                using var doc = JsonDocument.Parse(Payload(type));
                T.True(HookEventsWatcher.ToEvent(doc.RootElement, now) == null, type + " ignore");
            }
            using (var stop = JsonDocument.Parse("{\"hook_event_name\":\"Stop\",\"session_id\":\"s\",\"notification_type\":\"permission_prompt\"}"))
                T.True(HookEventsWatcher.ToEvent(stop.RootElement, now) == null, "autre hook ignore");
            using (var noSession = JsonDocument.Parse("{\"hook_event_name\":\"Notification\",\"notification_type\":\"permission_prompt\"}"))
                T.True(HookEventsWatcher.ToEvent(noSession.RootElement, now) == null, "sans session");
            using (var agent = JsonDocument.Parse(Payload("permission_prompt", agentId: "ag7")))
                T.Eq("ag7", HookEventsWatcher.ToEvent(agent.RootElement, now)!.AgentId, "agent_id transmis s'il existe");
        });

        T.Case("valeurs JSON concatenees, sur une ou plusieurs lignes", () =>
        {
            var w = new HookEventsWatcher(Path.Combine(Path.GetTempPath(), "inutilise.jsonl"));
            string data = Payload("permission_prompt", "s1") + Payload("permission_prompt", "s2") + "\n"
                          + Payload("agent_needs_input", "s3", indented: true) + "\n";
            var events = Parse(w, data, out int consumed);
            T.Eq("s1,s2,s3", string.Join(",", events.Select(e => e.SessionId)), "sessions");
            T.Eq(Encoding.UTF8.GetByteCount(data) - 1, consumed, "tout consomme sauf le blanc final");
        });

        T.Case("valeur coupee : attend la suite sans rien perdre", () =>
        {
            var w = new HookEventsWatcher(Path.Combine(Path.GetTempPath(), "inutilise.jsonl"));
            string full = Payload("permission_prompt", "cut", indented: true) + "\n";
            string first = full[..(full.Length / 2)];
            var events = Parse(w, first, out int consumed);
            T.Eq(0, events.Count, "rien encore");
            T.Eq(0, consumed, "rien consomme");
            events = Parse(w, full, out consumed);
            T.Eq("cut", events.Single().SessionId, "valeur complete");
        });

        T.Case("ligne illisible sautee, la suivante lue", () =>
        {
            var w = new HookEventsWatcher(Path.Combine(Path.GetTempPath(), "inutilise.jsonl"));
            string data = "{\"session_id\": \"tronque\", \"hook_ev\n" + "}}\n" + Payload("permission_prompt", "apres") + "\n";
            var events = Parse(w, data, out _);
            T.Eq("apres", events.Single().SessionId, "reprise apres le dechet");
        });

        T.Case("fichier absent : inactif sans erreur, puis lu depuis le debut quand il apparait", () =>
        {
            string dir = TempDir.Create("hook-absent");
            try
            {
                string path = Path.Combine(dir, "events.jsonl");
                var (w, c) = Start(path);
                using (w)
                {
                    Thread.Sleep(200);
                    T.True(w.Status.StartsWith("inactif"), "statut : " + w.Status);
                    File.AppendAllText(path, Payload("permission_prompt", "neuf") + "\n");
                    T.True(c.WaitFor(e => Needs(e, "neuf"), TimeSpan.FromSeconds(3)) != null, "premiere notification lue");
                    T.True(w.Status.StartsWith("actif"), "statut : " + w.Status);
                }
            }
            finally { TempDir.Delete(dir); }
        });

        T.Case("historique saute au demarrage, ajouts lus en moins de 200 ms", () =>
        {
            string dir = TempDir.Create("hook-hist");
            try
            {
                string path = Path.Combine(dir, "events.jsonl");
                File.WriteAllText(path, Payload("permission_prompt", "vieux") + "\n");
                var (w, c) = Start(path);
                using (w)
                {
                    Thread.Sleep(300);
                    long before = Stopwatch.GetTimestamp();
                    File.AppendAllText(path, Payload("elicitation_dialog", "recent") + "\n");
                    var got = c.WaitFor(e => Needs(e, "recent"), TimeSpan.FromSeconds(3));
                    T.True(got != null, "ajout lu");
                    double ms = Collector.Ms(before, got!.Value);
                    Console.WriteLine($"      latence hook : {ms:F0} ms");
                    T.True(ms < 200, $"latence {ms:F0} ms");
                    T.True(!c.All().Any(e => e.SessionId == "vieux"), "historique ignore");
                }
            }
            finally { TempDir.Delete(dir); }
        });

        T.Case("rotation au-dela du seuil : renomme en .1, le fichier recree est lu depuis le debut", () =>
        {
            string dir = TempDir.Create("hook-rotate");
            try
            {
                string path = Path.Combine(dir, "events.jsonl");
                File.WriteAllText(path, "");
                var (w, c) = Start(path, x => x.RotateBytes = 800);
                using (w)
                {
                    Thread.Sleep(300);
                    for (int i = 0; i < 6; i++) File.AppendAllText(path, Payload("permission_prompt", "r" + i) + "\n");
                    T.True(c.WaitFor(e => Needs(e, "r5"), TimeSpan.FromSeconds(3)) != null, "avant rotation");
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!File.Exists(path + ".1") && DateTime.UtcNow < deadline) Thread.Sleep(50);
                    T.True(File.Exists(path + ".1"), "events.jsonl.1 cree");
                    // le hook recree le fichier
                    File.AppendAllText(path, Payload("permission_prompt", "apres-rotation") + "\n");
                    T.True(c.WaitFor(e => Needs(e, "apres-rotation"), TimeSpan.FromSeconds(3)) != null, "nouveau fichier lu");
                    Thread.Sleep(300);
                    var all = c.All();
                    T.Eq(all.Count, all.Select(e => e.SessionId).Distinct().Count(), "aucune notification relue deux fois");
                }
            }
            finally { TempDir.Delete(dir); }
        });

        T.Case("fichier supprime puis recree : relu depuis le debut", () =>
        {
            string dir = TempDir.Create("hook-delete");
            try
            {
                string path = Path.Combine(dir, "events.jsonl");
                File.WriteAllText(path, "");
                var (w, c) = Start(path);
                using (w)
                {
                    Thread.Sleep(300);
                    File.AppendAllText(path, Payload("permission_prompt", "d1") + "\n" + Payload("permission_prompt", "d2") + "\n");
                    T.True(c.WaitFor(e => Needs(e, "d2"), TimeSpan.FromSeconds(3)) != null, "avant");
                    File.Delete(path);
                    Thread.Sleep(300);
                    // plus long que l'ancien : sans reouverture, le debut serait saute
                    File.WriteAllText(path, Payload("permission_prompt", "n1") + "\n" + Payload("permission_prompt", "n2") + "\n" + Payload("permission_prompt", "n3") + "\n");
                    T.True(c.WaitFor(e => Needs(e, "n1"), TimeSpan.FromSeconds(3)) != null, "debut du nouveau fichier");
                    T.True(c.WaitFor(e => Needs(e, "n3"), TimeSpan.FromSeconds(3)) != null, "fin du nouveau fichier");
                }
            }
            finally { TempDir.Delete(dir); }
        });

        T.Suite("HookInstaller et Hooks/");

        T.Case("EnsureHookScript : ecrit le script embarque en LF, puis ne le reecrit que s'il differe", () =>
        {
            string dir = Path.Combine(TempDir.Create("install"), ".hamster");
            try
            {
                string path = HookInstaller.EnsureHookScript(dir);
                T.Eq(Path.Combine(dir, "hook.sh"), path, "chemin");
                byte[] bytes = File.ReadAllBytes(path);
                T.True(!bytes.Contains((byte)'\r'), "aucun CR");
                T.True(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB), "pas de BOM");
                string text = Encoding.UTF8.GetString(bytes);
                T.True(text.StartsWith("#!/bin/sh\n"), "shebang");
                T.True(text.Contains("exec 2>/dev/null"), "stderr coupe");
                T.True(text.Contains("{ cat; printf '\\n'; } >> "), "ajout du payload et d'un saut de ligne");
                T.True(text.TrimEnd().EndsWith("exit 0"), "exit 0 final");

                var stamp = DateTime.UtcNow.AddMinutes(-5);
                File.SetLastWriteTimeUtc(path, stamp);
                HookInstaller.EnsureHookScript(dir);
                T.Eq(stamp, File.GetLastWriteTimeUtc(path), "identique : pas reecrit");

                File.WriteAllText(path, "echo modifie\n");
                HookInstaller.EnsureHookScript(dir);
                T.True(File.ReadAllBytes(path).SequenceEqual(bytes), "modifie : restaure");
            }
            finally { TempDir.Delete(Path.GetDirectoryName(dir)!); }
        });

        T.Case("script embarque = Hooks/hook.sh du depot (aux fins de ligne pres)", () =>
        {
            string repo = RepoRoot();
            string onDisk = File.ReadAllText(Path.Combine(repo, "Hooks", "hook.sh")).Replace("\r\n", "\n");
            T.Eq(onDisk, Encoding.UTF8.GetString(HookInstaller.ScriptBytes()), "contenu");
        });

        T.Case("Hooks/settings-snippet.json : bloc Notification attendu", () =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Hooks", "settings-snippet.json")));
            var entry = doc.RootElement.GetProperty("hooks").GetProperty("Notification")[0];
            T.Eq("permission_prompt|agent_needs_input|elicitation_dialog|elicitation_url_dialog", entry.GetProperty("matcher").GetString(), "matcher");
            var hook = entry.GetProperty("hooks")[0];
            T.Eq("command", hook.GetProperty("type").GetString(), "type");
            T.Eq("sh \"$HOME/.hamster/hook.sh\"", hook.GetProperty("command").GetString(), "commande");
            T.Eq(true, hook.GetProperty("async").GetBoolean(), "async");
            T.Eq(5, hook.GetProperty("timeout").GetInt32(), "timeout");
            // le matcher couvre exactement les types que le watcher retient
            T.True(entry.GetProperty("matcher").GetString()!.Split('|').ToHashSet().SetEquals(HookEventsWatcher.NeedsUserTypes), "memes types");
        });

        T.Case("hook.sh execute par sh : payload recopie + saut de ligne, code 0, lu par le watcher", () =>
        {
            string? sh = FindSh();
            if (sh == null)
            {
                Console.WriteLine("      sh introuvable : test saute");
                return;
            }
            string home = TempDir.Create("home");
            try
            {
                string script = HookInstaller.EnsureHookScript(Path.Combine(home, ".hamster"));
                string events = Path.Combine(home, ".hamster", "events.jsonl");
                var (w, c) = Start(events);
                using (w)
                {
                    Thread.Sleep(300);
                    string payload = Payload("permission_prompt", "via-sh", indented: true);
                    var sw = Stopwatch.StartNew();
                    int code = RunSh(sh, script, home, payload);
                    Console.WriteLine($"      sh hook.sh : {sw.ElapsedMilliseconds} ms");
                    T.Eq(0, code, "code de sortie");
                    T.Eq(payload.Replace("\r\n", "\n") + "\n", File.ReadAllText(events).Replace("\r\n", "\n"), "contenu ecrit");
                    T.True(c.WaitFor(e => Needs(e, "via-sh"), TimeSpan.FromSeconds(3)) != null, "lu par le watcher");
                }
                // HOME inutilisable (un fichier) : le hook ne doit ni echouer ni ecrire ailleurs
                string notADir = Path.Combine(home, "fichier");
                File.WriteAllText(notADir, "x");
                T.Eq(0, RunSh(sh, script, notADir, Payload("permission_prompt")), "code 0 malgre l'echec");
            }
            finally { TempDir.Delete(home); }
        });
    }

    static int RunSh(string sh, string script, string home, string stdin)
    {
        var psi = new ProcessStartInfo(sh)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(script);
        psi.Environment["HOME"] = home;
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15000)) { p.Kill(); return -1; }
        T.Eq("", output, "aucune sortie");
        return p.ExitCode;
    }

    static string? FindSh()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), "sh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        foreach (var candidate in new[] { @"C:\Program Files\Git\bin\sh.exe", @"C:\Program Files\Git\usr\bin\sh.exe" })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Hooks", "hook.sh"))) dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("racine du depot introuvable depuis " + AppContext.BaseDirectory);
    }
}
