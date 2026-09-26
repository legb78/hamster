using Hamster.App.Render;
using Hamster.Art;

namespace Hamster.App.Tests;

static class AnimatorTests
{
    static void Play(Animator a, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05) a.Advance(0.05);
    }

    public static void Run()
    {
        T.Suite("Animator");
        var lib = Shared.Library;

        T.Case("une boucle cede tout de suite au clip voulu", () =>
        {
            var a = new Animator(lib, Clips.Idle);
            Play(a, 0.3);
            a.Show(Clips.WorkBook);
            T.Eq(Clips.WorkBook, a.Current.Name, "clip");
            T.Eq(0, a.FrameIndex, "premiere frame");
        });

        T.Case("une erreur va au bout avant la scene de travail", () =>
        {
            var a = new Animator(lib, Clips.WorkBook);
            a.Show(Clips.Error);
            T.Eq(Clips.Error, a.Current.Name, "erreur lancee");
            Play(a, 1.0);
            a.Show(Clips.WorkBook);
            T.Eq(Clips.Error, a.Current.Name, "l'erreur continue");
            Play(a, lib[Clips.Error].FrameCount / (double)lib[Clips.Error].Fps);
            T.Eq(Clips.WorkBook, a.Current.Name, "puis la scene");
        });

        T.Case("le telephone coupe une erreur", () =>
        {
            var a = new Animator(lib, Clips.Idle);
            a.Show(Clips.Error);
            Play(a, 0.5);
            a.Show(Clips.Phone);
            T.Eq(Clips.Phone, a.Current.Name, "telephone");
        });

        T.Case("un one-shot fini et toujours voulu se fige", () =>
        {
            var a = new Animator(lib, Clips.Idle);
            a.Show(Clips.Celebrate);
            Play(a, 5);
            T.Eq(Clips.Celebrate, a.Current.Name, "fete");
            T.True(a.Finished, "fige");
            T.Eq(lib[Clips.Celebrate].FrameCount - 1, a.FrameIndex, "derniere frame");
        });

        T.Case("reaction au clic puis retour au clip voulu", () =>
        {
            var a = new Animator(lib, Clips.WorkLaptop);
            a.PlayOnce(Clips.React);
            T.Eq(Clips.React, a.Current.Name, "reaction");
            Play(a, 1.2);
            T.Eq(Clips.WorkLaptop, a.Current.Name, "retour");
        });
    }
}
