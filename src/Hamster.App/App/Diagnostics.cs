namespace Hamster.App;

/// <summary>
/// Les logs vont sur stderr : une app non signee ne remonte rien dans l'observateur
/// d'evenements, et on ne veut pas d'un fichier de log qui grossit tout seul. Seule
/// exception, bornee : cycle.log, voir Cycle.
/// </summary>
internal static class Diagnostics
{
    static readonly DateTime Start = DateTime.Now;

    public static void Info(string message) => Write("info", message);
    public static void Warn(string message) => Write("warn", message);

    /// <summary>
    /// Journal de cycle de vie : demarrage, arret et sa raison, veille, reprise. stderr
    /// n'est visible nulle part pour l'instance lancee a l'ouverture de session, et un
    /// arret silencieux ne laissait aucune trace. Quelques lignes par jour, coupe de
    /// moitie au-dela de 64 Ko : il ne grossit pas tout seul.
    /// </summary>
    public static void Cycle(string message)
    {
        Info(message);
        try
        {
            string dir = Settings.Directory;
            string path = Path.Combine(dir, "cycle.log");
            System.IO.Directory.CreateDirectory(dir);
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 64 * 1024)
            {
                var lines = File.ReadAllLines(path);
                File.WriteAllLines(path, lines[(lines.Length / 2)..]);
            }
            File.AppendAllText(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { /* journal de confort : jamais bloquant */ }
    }

    static void Write(string level, string message)
    {
        try
        {
            Console.Error.WriteLine($"[{(DateTime.Now - Start).TotalSeconds,7:F1}s] {level} {message}");
        }
        catch { /* pas de console attachee : tant pis */ }
    }
}
