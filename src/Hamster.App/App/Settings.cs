using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hamster.App;

/// <summary>Reglages persistes dans %APPDATA%\Hamster\settings.json.</summary>
internal sealed class Settings
{
    public int Scale { get; set; } = 2;
    public int OpacityPercent { get; set; } = 100;
    public bool Paused { get; set; }
    public bool DebugOverlay { get; set; }
    /// <summary>Position du centre du personnage, en pixels ecran physiques. Null = jamais place.</summary>
    public int? AnchorX { get; set; }
    public int? AnchorY { get; set; }
    /// <summary>Le hamster ne se montre que pendant que Claude Desktop tourne.</summary>
    public bool OnlyWithClaudeDesktop { get; set; } = true;
    /// <summary>
    /// Fragments de chemin qui designent le Claude.exe de Claude Desktop, et pas le
    /// claude.exe de Claude Code. A completer si Claude Desktop est installe ailleurs.
    /// </summary>
    public List<string> ClaudeDesktopPathMarkers { get; set; } = DefaultClaudeMarkers();

    static List<string> DefaultClaudeMarkers() => new() { @"\WindowsApps\Claude_" };

    [JsonIgnore]
    public byte Alpha => (byte)Math.Clamp(OpacityPercent * 255 / 100, 40, 255);

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hamster");

    static string Path_ => Path.Combine(Directory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path_));
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch (Exception e) { Diagnostics.Warn("settings illisibles, valeurs par defaut: " + e.Message); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Diagnostics.Warn("settings non sauvegardes: " + e.Message); }
    }

    void Normalize()
    {
        Scale = Math.Clamp(Scale, 1, 3);
        OpacityPercent = Math.Clamp(OpacityPercent, 20, 100);
        // un marqueur vide serait contenu dans tous les chemins, CLI compris
        ClaudeDesktopPathMarkers = (ClaudeDesktopPathMarkers ?? new())
            .Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (ClaudeDesktopPathMarkers.Count == 0) ClaudeDesktopPathMarkers = DefaultClaudeMarkers();
    }
}
