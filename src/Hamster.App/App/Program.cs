namespace Hamster.App;

internal static class Program
{
    /// <summary>Une seule instance par session : une seconde copie se ferme immediatement.</summary>
    const string MutexName = @"Local\Hamster.DesktopPet.SingleInstance";

    /// <summary>
    /// Signale par les scripts pour un arret propre. Un kill ne passe pas par Dispose :
    /// l'icone de notification resterait en fantome jusqu'au survol de la souris.
    /// </summary>
    public const string QuitEventName = @"Local\Hamster.DesktopPet.Quit";

    [STAThread]
    static int Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Diagnostics.Warn("une instance tourne deja, sortie");
            return 1;
        }

        // Avant toute fenetre : sans ca, Windows etire l'image sur un ecran a 150 %
        // et le pixel art devient flou. Ici on reste en pixels physiques et les
        // echelles sont des entiers, point.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diagnostics.Warn("exception non geree: " + e.ExceptionObject);

        // auto-reset : l'attente consomme le signal. Et si un script tient encore le
        // handle d'un signal pose pour l'instance precedente, on repart de zero
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        quit.Reset();

        using var context = new PetApplicationContext(quit);
        Application.Run(context);
        return 0;
    }
}
