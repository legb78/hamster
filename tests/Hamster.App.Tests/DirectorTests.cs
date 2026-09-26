using Hamster.Activity;
using Hamster.App.Render;
using Hamster.App.State;
using Hamster.Art;

namespace Hamster.App.Tests;

static class Shared
{
    /// <summary>Une seule bibliotheque pour tous les tests : la construire rend 148 frames.</summary>
    public static readonly SpriteLibrary Library = new();
}

static class DirectorTests
{
    static PetDirector New(int seed = 1) => new(Shared.Library.Has, new Random(seed));

    static readonly string[] WorkScenes = Clips.WorkPool.Select(p => p.Name).ToArray();
    static readonly string[] IdleScenes = Clips.IdlePool.Select(p => p.Name).ToArray();

    public static void Run()
    {
        T.Suite("PetDirector");

        T.Case("outil -> premiere scene de travail", () =>
        {
            var expected = new Dictionary<string, string>
            {
                ["Bash"] = Clips.WorkTerminal, ["PowerShell"] = Clips.WorkTerminal,
                ["Read"] = Clips.WorkBook, ["Grep"] = Clips.WorkBook, ["Glob"] = Clips.WorkBook,
                ["WebFetch"] = Clips.WorkBook, ["WebSearch"] = Clips.WorkBook,
                ["Edit"] = Clips.WorkLaptop, ["Write"] = Clips.WorkLaptop, ["NotebookEdit"] = Clips.WorkLaptop,
                ["Agent"] = Clips.WorkConductor, ["Workflow"] = Clips.WorkConductor,
            };
            foreach (var (tool, scene) in expected)
            {
                var d = New();
                d.Update(PetState.Working, tool, 0, 10);
                T.Eq(scene, d.Clip, tool);
                T.Eq(Movement.Hold, d.Movement, tool + " : pas de balade");
            }
        });

        T.Case("Agent sans work_conductor : repli sur work_laptop", () =>
        {
            var d = new PetDirector(n => n != Clips.WorkConductor && Shared.Library.Has(n), new Random(1));
            d.Update(PetState.Working, "Agent", 0, 10);
            T.Eq(Clips.WorkLaptop, d.Clip, "scene");
        });

        T.Case("scene tiree sans outil, remplacee par le premier outil qui arrive", () =>
        {
            var d = New();
            d.Update(PetState.Working, null, 0, 10);
            T.True(WorkScenes.Contains(d.Clip), "scene de travail : " + d.Clip);
            d.Update(PetState.Working, "Bash", 1, 10);
            T.Eq(Clips.WorkTerminal, d.Clip, "apres Bash");
            // un second outil ne change plus rien : seule la premiere scene suit l'outil
            d.Update(PetState.Working, "Read", 2, 10);
            T.Eq(Clips.WorkTerminal, d.Clip, "apres Read");
        });

        T.Case("rotation toutes les 8 a 18 s, jamais deux fois la meme scene", () =>
        {
            var d = New(7);
            d.Update(PetState.Working, "Edit", 0, 10);
            string current = d.Clip;
            double since = 0;
            var gaps = new List<double>();
            for (double t = 0.1; t < 5000; t += 0.1)
            {
                d.Update(PetState.Working, "Edit", t, 10);
                if (d.Clip == current) continue;
                gaps.Add(t - since);
                since = t;
                current = d.Clip;
                T.Eq(Movement.Hold, d.Movement, "pas de balade pendant le travail");
            }
            T.True(gaps.Count > 200, "assez de rotations : " + gaps.Count);
            T.True(gaps.Min() >= 8 - 1e-6 && gaps.Max() <= 18.1 + 1e-6, $"ecarts {gaps.Min():F1}..{gaps.Max():F1} s");
        });

        T.Case("tirage pondere : poids du contrat B, think a 5", () =>
        {
            var d = New(3);
            d.Update(PetState.Working, "Edit", 0, 10);
            var count = WorkScenes.ToDictionary(s => s, _ => 0);
            string current = d.Clip;
            int n = 0;
            for (double t = 0.5; n < 20000; t += 0.5)
            {
                d.Update(PetState.Working, "Edit", t, 10);
                if (d.Clip == current) continue;
                T.True(d.Clip != current, "deux fois la meme scene");
                current = d.Clip;
                count[current]++;
                n++;
            }
            // chaine de Markov : p(j|i) = w_j / (W - w_i). Sa loi stationnaire est pi_i ~ w_i (W - w_i)
            var w = Clips.WorkPool.ToDictionary(p => p.Name, p => (double)p.Weight);
            double W = w.Values.Sum();
            double z = w.Sum(kv => kv.Value * (W - kv.Value));
            foreach (var (scene, c) in count)
            {
                double expected = w[scene] * (W - w[scene]) / z;
                double got = c / (double)n;
                T.Info($"{scene,-15} {got:P1} (attendu {expected:P1})");
                T.True(Math.Abs(got - expected) < 0.015, $"{scene} : {got:P1} au lieu de {expected:P1}");
            }
            T.True(count[Clips.Think] / (double)n < 0.06, "think reste rare");
        });

        T.Case("attente, fete, erreur : leurs clips, sans balade", () =>
        {
            var d = New();
            d.Update(PetState.WaitingUser, null, 0, 10);
            T.Eq(Clips.Phone, d.Clip, "attente");
            T.Eq(Movement.Hold, d.Movement, "attente immobile");
            d.Update(PetState.Celebrating, null, 1, 10);
            T.Eq(Clips.Celebrate, d.Clip, "fete");
            d.Update(PetState.Error, null, 2, 10);
            T.Eq(Clips.Error, d.Clip, "erreur");
            T.Eq(Movement.Hold, d.Movement, "erreur immobile");
            for (double t = 3; t < 400; t += 0.5)
            {
                d.Update(PetState.WaitingUser, null, t, 10);
                T.Eq(Movement.Hold, d.Movement, "attente longue immobile");
            }
        });

        T.Case("apres une erreur, la scene de travail reprend", () =>
        {
            var d = New();
            d.Update(PetState.Working, "Read", 0, 10);
            d.Update(PetState.Error, "Read", 2, 10);
            d.Update(PetState.Working, "Read", 4, 10);
            T.Eq(Clips.WorkBook, d.Clip, "scene reprise");
        });

        T.Case("repos : balade libre avant le delai de chill, puis activites", () =>
        {
            var d = New(5);
            d.Update(PetState.Idle, null, 0, 10);
            for (double t = 0; t < 9.9; t += 0.1)
            {
                d.Update(PetState.Idle, null, t, 10);
                T.Eq(Movement.Roam, d.Movement, "balade avant le delai");
            }
            d.Update(PetState.Idle, null, 10.05, 10);
            T.True(IdleScenes.Contains(d.Clip), "activite de repos apres 10 s : " + d.Clip);
        });

        T.Case("repos : 6 a 15 s par activite, 1,5 a 3 s de pose, parfois une petite balade", () =>
        {
            var d = New(11);
            d.Update(PetState.Idle, null, 0, 5);
            string phase = "", clip = "";
            double since = 0;
            var variant = new List<double>();
            var neutral = new List<double>();
            int strolls = 0;
            var seen = new HashSet<string>();
            for (double t = 0; t < 20000; t += 0.05)
            {
                d.Update(PetState.Idle, null, t, 5);
                if (d.Phase == phase && d.Clip == clip) continue;
                if (phase == "chill Variant") variant.Add(t - since);
                if (phase == "chill Neutral") neutral.Add(t - since);
                if (d.Phase == "chill Stroll")
                {
                    strolls++;
                    T.Eq(Clips.Walk, d.Clip, "petite balade en marchant");
                    T.Eq(Movement.Walk, d.Movement, "petite balade");
                }
                if (d.Phase == "chill Neutral") T.Eq(Clips.Idle, d.Clip, "pose neutre");
                if (d.Phase == "chill Variant")
                {
                    seen.Add(d.Clip);
                    // seul le skate roule
                    T.Eq(d.Clip == Clips.IdleWheel ? Movement.Walk : Movement.Hold, d.Movement, "mouvement de " + d.Clip);
                }
                phase = d.Phase;
                clip = d.Clip;
                since = t;
            }
            T.True(variant.Min() >= 6 - 0.06 && variant.Max() <= 15 + 0.06, $"activites {variant.Min():F1}..{variant.Max():F1} s");
            T.True(neutral.Min() >= 1.5 - 0.06 && neutral.Max() <= 3 + 0.06, $"poses {neutral.Min():F1}..{neutral.Max():F1} s");
            T.True(strolls > 10, "petites balades : " + strolls);
            T.Eq(IdleScenes.Length, seen.Count, "toutes les activites de repos sortent");
        });

        T.Case("reprise du travail : plus de balade", () =>
        {
            var d = New();
            d.Update(PetState.Idle, null, 0, 10);
            d.Update(PetState.Idle, null, 30, 10);
            d.Update(PetState.Working, "Bash", 31, 10);
            T.Eq(Movement.Hold, d.Movement, "travail");
            T.Eq(Clips.WorkTerminal, d.Clip, "scene");
        });
    }
}
