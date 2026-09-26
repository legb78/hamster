using System.Diagnostics;

namespace Hamster.Activity.Tests;

/// <summary>
/// Relit les vrais transcripts du disque, en lecture seule, avec le meme decoupage que le
/// watcher. Verifie seulement : aucune exception, aucune ligne orpheline (ligne qui n'est pas
/// du JSON, ou fin de fichier sans saut de ligne sur un fichier que plus personne n'ecrit).
/// N'affiche que des compteurs, jamais de contenu : ce sont des donnees privees.
/// </summary>
static class ReplayTest
{
    public static void Run()
    {
        T.Suite("Relecture des vrais transcripts (lecture seule)");
        string root = TranscriptPaths.DefaultProjectsRoot;
        if (!Directory.Exists(root))
        {
            Console.WriteLine("      " + root + " absent : relecture sautee");
            return;
        }

        T.Case("aucune exception, aucune ligne orpheline", () =>
        {
            var sw = Stopwatch.StartNew();
            long interpretErrorsBefore = Interlocked.Read(ref TranscriptParser.InterpretErrors);
            var model = new ActivityModel();
            var byKind = new SortedDictionary<ActivityKind, long>();
            long files = 0, mainFiles = 0, agentFiles = 0, ignored = 0, withLabel = 0, foreground = 0, bytes = 0, lines = 0, backwards = 0;
            long invalid = 0, trailing = 0, trailingActive = 0, unmatchedResults = 0, exceptions = 0, skippedBusy = 0, lateApiErrors = 0;
            var scratch = new byte[256 * 1024];

            foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var source = TranscriptPaths.Classify(path, root);
                if (source == null) { ignored++; continue; }
                files++;
                if (source.AgentId == null) mainFiles++; else agentFiles++;
                if (source.AgentDescription != null) withLabel++;
                if (source.ForegroundToolUseId != null) foreground++;

                var started = new HashSet<string>(StringComparer.Ordinal);
                var latest = DateTimeOffset.MinValue;
                var lastEnd = DateTimeOffset.MinValue;
                var tail = new TailFile(path, 0);
                try
                {
                    while (true)
                    {
                        var read = tail.Read(scratch, long.MaxValue, chunk =>
                        {
                            bytes += chunk.Length;
                            tail.FeedLines(chunk, line =>
                            {
                                lines++;
                                var events = TranscriptParser.ParseLine(line, source, out bool valid);
                                if (!valid) invalid++;
                                foreach (var e in events)
                                {
                                    // evenement plus ancien qu'un precedent du meme fichier : le tri de l'amorcage le deplace
                                    if (e.Time < latest) backwards++; else latest = e.Time;
                                    byKind[e.Kind] = byKind.GetValueOrDefault(e.Kind) + 1;
                                    if (e.Kind is ActivityKind.ToolStarted or ActivityKind.AskedUser && e.Detail != null) started.Add(e.Detail);
                                    if (e.Kind == ActivityKind.ToolFinished && (e.Detail == null || !started.Contains(e.Detail))) unmatchedResults++;
                                    // api_error du fil principal date d'avant une fin de tour deja lue : le modele l'ignore
                                    if (e.AgentId == null && e.Kind == ActivityKind.ApiError && e.Time <= lastEnd) lateApiErrors++;
                                    if (e.AgentId == null && e.Kind == ActivityKind.TurnEnded && e.Time > lastEnd) lastEnd = e.Time;
                                    model.Apply(e);
                                }
                            });
                        }, out bool more);
                        if (read == TailRead.Busy) { skippedBusy++; break; }
                        if (!more) break;
                    }
                }
                catch (Exception) { exceptions++; }

                if (tail.PendingLength > 0)
                {
                    // un fichier en cours d'ecriture peut legitimement finir sur une demi-ligne
                    bool active = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(30);
                    if (active) trailingActive++; else trailing++;
                }
            }

            ActivitySnapshot? snap = null;
            try { snap = model.Snapshot(DateTimeOffset.UtcNow); }
            catch (Exception) { exceptions++; }

            Console.WriteLine($"      {files} transcripts ({mainFiles} sessions, {agentFiles} sous-agents dont {withLabel} avec libelle et {foreground} au premier plan avec toolUseId), {ignored} autres .jsonl ignores");
            Console.WriteLine($"      {bytes / (1024.0 * 1024):F1} Mo, {lines} lignes en {sw.Elapsed.TotalSeconds:F1} s");
            Console.WriteLine("      evenements : " + string.Join(", ", byKind.Select(kv => $"{kv.Key} {kv.Value}")));
            Console.WriteLine($"      lignes non JSON {invalid}, fins sans saut de ligne {trailing} (+{trailingActive} fichiers en cours d'ecriture), fichiers occupes {skippedBusy}");
            Console.WriteLine($"      tool_result sans tool_use dans le meme fichier (information) : {unmatchedResults}");
            Console.WriteLine($"      evenements plus anciens qu'un precedent du meme fichier (information) : {backwards}");
            Console.WriteLine($"      api_error du fil principal lus apres la fin du tour et dates d'avant elle, ignores (information) : {lateApiErrors} sur {byKind.GetValueOrDefault(ActivityKind.ApiError)} ApiError");
            if (snap != null)
                Console.WriteLine($"      etat du modele a la fin : {snap.State}, {snap.Minis.Count} minis (+{snap.MinisOverflow}), session active : {snap.AnySessionActive}");

            long interpretErrors = Interlocked.Read(ref TranscriptParser.InterpretErrors) - interpretErrorsBefore;
            Console.WriteLine($"      exceptions : {exceptions} hors parseur, {interpretErrors} avalees par le parseur");
            T.Eq(0L, exceptions, "exceptions");
            T.Eq(0L, interpretErrors, "exceptions avalees par le parseur");
            T.Eq(0L, invalid, "lignes non JSON");
            T.Eq(0L, trailing, "fins de fichier sans saut de ligne");
        });
    }
}
