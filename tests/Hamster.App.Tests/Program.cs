using System.Diagnostics;

namespace Hamster.App.Tests;

/// <summary>
/// Tests de l'app sans paquet NuGet : une application console qui sort en code 1 au moindre echec.
///   dotnet run --project tests/Hamster.App.Tests -c Release
/// Aucune fenetre n'est ouverte : on teste la logique (directeur, minis, rendu, registre),
/// l'app elle-meme se teste en vrai, lancee avec des dossiers temporaires.
/// </summary>
static class Program
{
    [STAThread]
    static int Main()
    {
        var total = Stopwatch.StartNew();
        DirectorTests.Run();
        AnimatorTests.Run();
        CrowdTests.Run();
        RenderTests.Run();
        AppTests.Run();

        Console.WriteLine();
        Console.WriteLine($"{T.Passed} ok, {T.Failed} en echec, {total.Elapsed.TotalSeconds:F1} s");
        return T.Failed == 0 ? 0 : 1;
    }
}

/// <summary>Assertions minimales. Un echec leve une exception attrapee par Case.</summary>
static class T
{
    public static int Passed, Failed;

    public static void Case(string name, Action body)
    {
        try
        {
            body();
            Passed++;
            Console.WriteLine("ok    " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Console.WriteLine("ECHEC " + name + " : " + e.Message);
        }
    }

    public static void Suite(string name) => Console.WriteLine("\n== " + name);

    public static void Eq<TV>(TV expected, TV actual, string what)
    {
        if (!EqualityComparer<TV>.Default.Equals(expected, actual))
            throw new Exception($"{what} : attendu <{expected}>, obtenu <{actual}>");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    public static void Info(string text) => Console.WriteLine("      " + text);
}
