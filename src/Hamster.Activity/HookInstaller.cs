using System.Text;

namespace Hamster.Activity;

/// <summary>
/// Depose le script du hook dans ~/.hamster. Ne touche jamais a ~/.claude : brancher le hook
/// dans les reglages de Claude Code reste un geste de l'utilisateur (Hooks/settings-snippet.json).
/// </summary>
public static class HookInstaller
{
    const string ResourceName = "Hamster.Activity.hook.sh";

    /// <summary>Ecrit ~/.hamster/hook.sh s'il est absent ou different. Rend son chemin.</summary>
    public static string EnsureHookScript() =>
        EnsureHookScript(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hamster"));

    internal static string EnsureHookScript(string directory)
    {
        byte[] script = ScriptBytes();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hook.sh");
        try
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(script)) return path;
        }
        catch (IOException) { /* illisible : on le reecrit */ }

        // ecrit a cote puis renomme : un hook lance a cet instant ne lit jamais un script a moitie ecrit
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, script);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Contenu du script, en fins de ligne LF : sh (Git Bash) lit un \r comme faisant partie
    /// de la commande, et un checkout Windows peut avoir converti le fichier.
    /// </summary>
    internal static byte[] ScriptBytes()
    {
        using var stream = typeof(HookInstaller).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("ressource " + ResourceName + " absente");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = reader.ReadToEnd().Replace("\r\n", "\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
    }
}
