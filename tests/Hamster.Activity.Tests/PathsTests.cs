namespace Hamster.Activity.Tests;

static class PathsTests
{
    public static void Run()
    {
        T.Suite("TranscriptPaths");
        string root = TempDir.Create("paths");
        const string session = "0f0e0d0c-0b0a-0908-0706-050403020100";
        try
        {
            string slug = Path.Combine(root, "C--work-demo");
            string sub = Path.Combine(slug, session, "subagents");
            string wf = Path.Combine(sub, "workflows", "wf_1234");
            Directory.CreateDirectory(wf);

            T.Case("session principale : slug\\<guid>.jsonl", () =>
            {
                var s = TranscriptPaths.Classify(Path.Combine(slug, session + ".jsonl"), root);
                T.Eq(new TranscriptSource(session, null, null), s, "source");
            });

            T.Case("fichier quelconque au niveau session : ignore", () =>
            {
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify(Path.Combine(slug, "notes.jsonl"), root), "pas un guid");
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify(Path.Combine(slug, session + ".json"), root), "extension");
            });

            T.Case("sous-agent : description du meta.json", () =>
            {
                File.WriteAllText(Path.Combine(sub, "agent-abc123.meta.json"),
                    "{\"agentType\":\"Explore\",\"description\":\"Chercher les appels\",\"toolUseId\":\"toolu_1\",\"spawnDepth\":1}");
                var s = TranscriptPaths.Classify(Path.Combine(sub, "agent-abc123.jsonl"), root);
                T.Eq(new TranscriptSource(session, "abc123", "Chercher les appels"), s, "source");
            });

            T.Case("sous-agent : agentType a defaut de description", () =>
            {
                File.WriteAllText(Path.Combine(sub, "agent-def456.meta.json"), "{\"agentType\":\"general-purpose\",\"spawnDepth\":1}");
                T.Eq("general-purpose", TranscriptPaths.Classify(Path.Combine(sub, "agent-def456.jsonl"), root)?.AgentDescription, "libelle");
            });

            T.Case("sous-agent : toolUseId retenu seulement au premier plan (requestShape present et different de background)", () =>
            {
                string Fg(string id, string meta)
                {
                    File.WriteAllText(Path.Combine(sub, "agent-" + id + ".meta.json"), meta);
                    return TranscriptPaths.Classify(Path.Combine(sub, "agent-" + id + ".jsonl"), root)?.ForegroundToolUseId ?? "null";
                }
                T.Eq("toolu_f", Fg("fore", "{\"agentType\":\"Explore\",\"requestShape\":\"foreground\",\"toolUseId\":\"toolu_f\"}"), "foreground");
                T.Eq("toolu_n", Fg("neuf", "{\"agentType\":\"Explore\",\"requestShape\":\"autre-forme\",\"toolUseId\":\"toolu_n\"}"), "autre forme que background");
                T.Eq("null", Fg("back", "{\"agentType\":\"Explore\",\"requestShape\":\"background\",\"toolUseId\":\"toolu_b\"}"), "background");
                // mesure sur les vrais transcripts : sans requestShape, 9 agents sur 11 recoivent ce resultat des le lancement
                T.Eq("null", Fg("vieux", "{\"agentType\":\"Explore\",\"toolUseId\":\"toolu_v\"}"), "requestShape absent");
                T.Eq("null", Fg("sansid", "{\"agentType\":\"Explore\",\"requestShape\":\"foreground\"}"), "toolUseId absent");
                T.Eq("Explore", TranscriptPaths.Classify(Path.Combine(sub, "agent-fore.jsonl"), root)?.AgentDescription, "libelle inchange");
            });

            T.Case("sous-agent sans meta.json, ou meta illisible : libelle null", () =>
            {
                T.Eq(new TranscriptSource(session, "nometa", null), TranscriptPaths.Classify(Path.Combine(sub, "agent-nometa.jsonl"), root), "sans meta");
                File.WriteAllText(Path.Combine(sub, "agent-bad.meta.json"), "{casse");
                T.Eq<string?>(null, TranscriptPaths.Classify(Path.Combine(sub, "agent-bad.jsonl"), root)?.AgentDescription, "meta casse");
            });

            T.Case("agent de workflow : subagents\\workflows\\<wf>\\agent-<id>.jsonl", () =>
            {
                var s = TranscriptPaths.Classify(Path.Combine(wf, "agent-wf01.jsonl"), root);
                T.Eq(session, s?.SessionId, "session");
                T.Eq("wf01", s?.AgentId, "agent");
            });

            T.Case("journal.jsonl des workflows : pas un agent", () =>
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify(Path.Combine(wf, "journal.jsonl"), root), "journal"));

            T.Case("hors de la racine, ou chemin absurde : null sans exception", () =>
            {
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify(Path.Combine(Path.GetTempPath(), session + ".jsonl"), root), "hors racine");
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify("", root), "vide");
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify("\0<>|", root), "caracteres interdits");
                T.Eq<TranscriptSource?>(null, TranscriptPaths.Classify(Path.Combine(root, "x.jsonl"), root), "trop haut");
            });

            T.Case("DefaultProjectsRoot : HAMSTER_PROJECTS_ROOT, sinon ~/.claude/projects", () =>
            {
                string? saved = Environment.GetEnvironmentVariable("HAMSTER_PROJECTS_ROOT");
                try
                {
                    Environment.SetEnvironmentVariable("HAMSTER_PROJECTS_ROOT", @"D:\ailleurs");
                    T.Eq(@"D:\ailleurs", TranscriptPaths.DefaultProjectsRoot, "variable");
                    Environment.SetEnvironmentVariable("HAMSTER_PROJECTS_ROOT", null);
                    string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
                    T.Eq(expected, TranscriptPaths.DefaultProjectsRoot, "defaut");
                }
                finally { Environment.SetEnvironmentVariable("HAMSTER_PROJECTS_ROOT", saved); }
            });
        }
        finally { TempDir.Delete(root); }
    }
}

static class TempDir
{
    public static string Create(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "hamster-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        for (int i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); return; }
            catch (IOException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }
}
