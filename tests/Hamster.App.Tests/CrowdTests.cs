using Hamster.Activity;
using Hamster.App.State;
using Hamster.Art;

namespace Hamster.App.Tests;

static class CrowdTests
{
    static MiniInfo Info(string id, string kind = "subagent", PetState state = PetState.Working) =>
        new(id, kind, "mini " + id, id, state, DateTimeOffset.UnixEpoch);

    static void Advance(MiniCrowd c, ref double now, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05) { now += 0.05; c.Advance(0.05, now); }
    }

    public static void Run()
    {
        T.Suite("MiniCrowd");

        T.Case("apparition : fumee, personnage cache sous les deux premieres frames", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            double now = 100;
            var born = c.Sync(new[] { Info("a1") }, 0, now);
            T.Eq(1, born.Count, "ne");
            var m = c.Minis[0];
            T.Eq(Clips.FxPop, m.Fx?.Current.Name, "fumee");
            T.True(!m.ShowsBody, "cache au debut");
            Advance(c, ref now, 0.2);
            T.True(m.ShowsBody, "visible des la troisieme frame");
            Advance(c, ref now, 0.5);
            T.True(m.Fx == null, "fumee finie");
        });

        T.Case("disparition : etincelles, puis plus rien", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            double now = 0;
            c.Sync(new[] { Info("a1") }, 0, now);
            Advance(c, ref now, 1);
            c.Sync(Array.Empty<MiniInfo>(), 0, now);
            T.Eq(1, c.Minis.Count, "encore la le temps des etincelles");
            T.Eq(Clips.FxSparkle, c.Minis[0].Fx?.Current.Name, "etincelles");
            T.True(!c.Minis[0].ShowsBody, "personnage parti");
            Advance(c, ref now, 0.7);
            T.Eq(0, c.Minis.Count, "fini");
        });

        T.Case("cache ou en pause : ni fumee ni etincelles", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            c.Sync(new[] { Info("a1") }, 0, 0, animate: false);
            T.True(c.Minis[0].Fx == null && c.Minis[0].ShowsBody, "pas de fumee");
            c.Sync(Array.Empty<MiniInfo>(), 0, 1, animate: false);
            T.Eq(0, c.Minis.Count, "retire tout de suite");
        });

        T.Case("scene stable par id, jamais think", () =>
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                string id = "agent" + i.ToString("x");
                string scene = MiniCrowd.SceneFor(id);
                T.Eq(scene, MiniCrowd.SceneFor(id), "stable");
                T.True(scene != Clips.Think, "pas think");
                seen.Add(scene);
            }
            T.Eq(5, seen.Count, "les cinq scenes de travail sortent");
        });

        T.Case("clip selon l'etat, noeud selon ColorKey", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            c.Sync(new[] { Info("s1", "session", PetState.WaitingUser) }, 0, 0);
            var m = c.Minis[0];
            T.Eq(Clips.Phone, MiniCrowd.ClipFor(m), "attente");
            T.Eq(Palette.HueIndexFor("s1"), m.Hue, "teinte");
            m.State = PetState.Error;
            T.Eq(Clips.Error, MiniCrowd.ClipFor(m), "erreur");
            m.State = PetState.Working;
            T.Eq(m.Scene, MiniCrowd.ClipFor(m), "travail");
        });

        T.Case("les minis se repartissent regulierement sur l'orbite", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            double now = 0;
            c.Sync(new[] { Info("a1") }, 0, now);
            Advance(c, ref now, 3);
            c.Sync(new[] { Info("a1"), Info("a2") }, 0, now);
            c.Sync(new[] { Info("a1"), Info("a2"), Info("a3") }, 0, now);
            Advance(c, ref now, 10);
            var angles = c.Minis.Select(m => Norm(c.AngleOf(m, now))).OrderBy(a => a).ToArray();
            for (int i = 0; i < 3; i++)
            {
                double gap = Norm(angles[(i + 1) % 3] - angles[i]);
                T.True(Math.Abs(gap - 2 * Math.PI / 3) < 0.02, $"ecart {gap:F3} rad");
            }
        });

        T.Case("clic sur un mini : sa reaction, puis sa scene", () =>
        {
            var c = new MiniCrowd(Shared.Library);
            double now = 0;
            c.Sync(new[] { Info("a1") }, 0, now);
            c.React("a1");
            T.Eq(Clips.React, c.Minis[0].Body.Current.Name, "reaction");
            Advance(c, ref now, 1.5);
            T.Eq(c.Minis[0].Scene, c.Minis[0].Body.Current.Name, "retour a la scene");
        });
    }

    static double Norm(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + 2 * Math.PI : a;
    }
}
