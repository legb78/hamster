using System.Diagnostics;

namespace Hamster.Activity.Tests;

/// <summary>
/// Tests sans paquet NuGet : une application console qui sort en code 1 au premier echec.
///   dotnet run --project tests/Hamster.Activity.Tests -c Release              tout
///   dotnet run --project tests/Hamster.Activity.Tests -c Release -- --no-replay   sans la relecture des vrais transcripts
///   dotnet run --project tests/Hamster.Activity.Tests -c Release -- --replay-only
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        bool replay = !args.Contains("--no-replay");
        bool only = args.Contains("--replay-only");
        var total = Stopwatch.StartNew();

        if (!only)
        {
            ParserTests.Run();
            PathsTests.Run();
            ModelTests.Run();
            WatcherTests.Run();
            HookTests.Run();
        }
        if (replay || only) ReplayTest.Run();

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
}
