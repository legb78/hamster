using System.Diagnostics;
using static Hamster.App.Window.Native;

namespace Hamster.App;

/// <summary>
/// Dit si Claude Desktop tourne. Le nom de processus ne suffit pas : Claude Code
/// (CLI, extension VS Code) s'appelle lui aussi "claude". On lit donc le chemin de
/// l'image de chaque processus "claude" et on y cherche un des marqueurs des
/// reglages -- par defaut le dossier du paquet MSIX, WindowsApps\Claude_.
/// </summary>
internal static class ClaudeDesktop
{
    public static bool IsRunning(IReadOnlyList<string> pathMarkers)
    {
        var processes = Process.GetProcessesByName("claude");
        try
        {
            foreach (var process in processes)
            {
                string? path = ImagePath(process.Id);
                if (path == null) continue;
                foreach (var marker in pathMarkers)
                    if (path.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    static string? ImagePath(int processId)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        // deja termine entre l'enumeration et l'ouverture, ou protege : on l'ignore
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            int size = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally { CloseHandle(handle); }
    }
}
