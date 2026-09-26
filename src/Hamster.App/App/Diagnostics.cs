namespace Hamster.App;

/// <summary>
/// Les logs vont sur stderr : une app non signee ne remonte rien dans l'observateur
/// d'evenements, et on ne veut pas d'un fichier de log qui grossit tout seul.
/// </summary>
internal static class Diagnostics
{
    static readonly DateTime Start = DateTime.Now;

    public static void Info(string message) => Write("info", message);
    public static void Warn(string message) => Write("warn", message);

    static void Write(string level, string message)
    {
        try
        {
            Console.Error.WriteLine($"[{(DateTime.Now - Start).TotalSeconds,7:F1}s] {level} {message}");
        }
        catch { /* pas de console attachee : tant pis */ }
    }
}
