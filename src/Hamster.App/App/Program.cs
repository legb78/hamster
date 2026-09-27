using Microsoft.Win32;

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

    /// <summary>Pose par le menu ou le signal d'arret, ecrite dans cycle.log a la sortie.</summary>
    internal static string? ExitReason;

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

        Diagnostics.Cycle("demarrage " + Environment.ProcessPath);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = e.ExceptionObject?.ToString() ?? "?";
            Diagnostics.Cycle("exception non geree: " + (text.Length > 2000 ? text[..2000] : text));
        };
        // un kill ne passe par aucune de ces lignes : son absence dans cycle.log le signe
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Diagnostics.Cycle("sortie du processus");
        SystemEvents.SessionEnding += (_, e) => Diagnostics.Cycle("fin de session Windows: " + e.Reason);
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode is PowerModes.Suspend or PowerModes.Resume) Diagnostics.Cycle("alimentation: " + e.Mode);
        };

        // auto-reset : l'attente consomme le signal. Et si un script tient encore le
        // handle d'un signal pose pour l'instance precedente, on repart de zero
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        quit.Reset();

        using var context = new PetApplicationContext(quit);
        Application.Run(context);
        Diagnostics.Cycle("arret: " + (ExitReason ?? "boucle de messages terminee sans raison connue"));
        return 0;
    }
}
