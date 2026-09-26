using System.Text.Json;
using Hamster.Activity;

namespace Hamster.App.Tests;

/// <summary>
/// ActivityHub branche pour de vrai : les deux watchers sur des dossiers temporaires, les lots
/// ramenes sur le thread UI par une fenetre cachee et la boucle de messages pompee a la main.
/// Le script du hook n'est pas depose (StartWatchers) : rien n'est ecrit dans ~/.hamster.
/// </summary>
static class HubTests
{
    public static void Run()
    {
        T.Suite("ActivityHub");

        T.Case("demande du hook d'une conversation sans transcript : ecartee et comptee (\"1 ignorees\"), l'autre passe", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "hamster-hub-tests-" + Guid.NewGuid().ToString("N"));
            string projects = Path.Combine(dir, "projects");
            string events = Path.Combine(dir, "events.jsonl");
            string known = Guid.NewGuid().ToString(), unknown = Guid.NewGuid().ToString();
            using var ui = new Control();
            ui.CreateControl();
            ActivityHub? hub = null;
            try
            {
                string slug = Path.Combine(projects, "C--work-demo");
                Directory.CreateDirectory(slug);
                // une conversation au travail : son prompt vient d'etre ecrit
                File.WriteAllText(Path.Combine(slug, known + ".jsonl"), Prompt(known, DateTimeOffset.UtcNow.AddSeconds(-2)) + "\n");
                File.WriteAllText(events, "");

                hub = new ActivityHub(ui, projects, events);
                hub.StartWatchers();
                var h = hub;
                T.True(Pump(() => h.Snapshot.MainSessionId == known), "transcript lu : " + h.TranscriptStatus);
                T.True(Pump(() => h.HookStatus.StartsWith("actif")), "hook : " + h.HookStatus);

                File.AppendAllText(events, Notification(unknown) + "\n" + Notification(known) + "\n");
                T.True(Pump(() => h.Snapshot.State == PetState.WaitingUser), "la conversation connue attend : " + h.HookStatus);
                T.True(Pump(() => h.HookStatus.Contains("2 notifications lues")), "statut : " + h.HookStatus);
                T.True(h.HookStatus.Contains("1 demandes") && h.HookStatus.Contains("1 ignorees"), "statut : " + h.HookStatus);
                T.Eq(known, h.Snapshot.MainSessionId, "principale");
                T.True(h.Snapshot.Minis.Count == 0, "rien pour l'inconnue");
            }
            finally
            {
                hub?.Dispose();
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });
    }

    /// <summary>Pompe la boucle de messages jusqu'a ce que done soit vrai, 5 s au plus.</summary>
    static bool Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            if (done()) return true;
            Thread.Sleep(20);
        }
        Application.DoEvents();
        return done();
    }

    /// <summary>Ligne de transcript synthetique, a la forme observee (aucun contenu reel).</summary>
    static string Prompt(string session, DateTimeOffset at) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = "user",
        ["sessionId"] = session,
        ["timestamp"] = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        ["cwd"] = @"C:\work\demo",
        ["uuid"] = Guid.NewGuid().ToString(),
        ["message"] = new { role = "user", content = "fais ceci" },
    });

    static string Notification(string session) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["session_id"] = session,
        ["cwd"] = @"C:\work\demo",
        ["hook_event_name"] = "Notification",
        ["message"] = "Claude a besoin de vous",
        ["notification_type"] = "permission_prompt",
    });
}
