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
    /// Seul reglage edite a la main : il est relu a chaud, voir RefreshMarkersFromDisk.
    /// </summary>
    public List<string> ClaudeDesktopPathMarkers { get; set; } = DefaultClaudeMarkers();

    static List<string> DefaultClaudeMarkers() => new() { @"\WindowsApps\Claude_" };

    [JsonIgnore]
    public byte Alpha => (byte)Math.Clamp(OpacityPercent * 255 / 100, 40, 255);

    /// <summary>Message de la derniere lecture ratee, null si tout va bien.</summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    /// <summary>Leve quand un settings.json edite a la main se revele illisible en cours de route.</summary>
    public event Action<string>? Unreadable;

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hamster");

    static string Path_ => Path.Combine(Directory, "settings.json");
    static string BadPath => Path.Combine(Directory, "settings.json.bad");

    // le fichier s'edite a la main : on tolere commentaires, virgule finale et casse
    // des noms, les fautes les plus courantes, plutot que de tout remettre a zero
    static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    // date d'ecriture du fichier a la derniere lecture ou ecriture de notre part
    DateTime _seenWriteUtc;

    public static Settings Load()
    {
        var s = ReadDisk(out string? error) ?? new Settings();
        s.LoadError = error;
        s._seenWriteUtc = Stamp();
        return s;
    }

    /// <summary>
    /// Reprend les marqueurs du disque si le fichier a change depuis notre derniere lecture
    /// ou ecriture : l'instance lancee a l'ouverture de session tourne deja quand on edite
    /// le fichier. Vrai si la liste a change.
    /// </summary>
    public bool RefreshMarkersFromDisk()
    {
        var stamp = Stamp();
        if (stamp == _seenWriteUtc) return false;
        _seenWriteUtc = stamp;

        var disk = ReadDisk(out string? error);
        if (error != null)
        {
            LoadError = error;
            Unreadable?.Invoke(error);
            return false;
        }
        if (disk == null || disk.ClaudeDesktopPathMarkers.SequenceEqual(ClaudeDesktopPathMarkers)) return false;

        ClaudeDesktopPathMarkers = disk.ClaudeDesktopPathMarkers;
        Diagnostics.Info("marqueurs Claude Desktop relus: " + string.Join(", ", ClaudeDesktopPathMarkers));
        return true;
    }

    public void Save()
    {
        // une edition a la main pas encore relue serait ecrasee par la version en memoire
        RefreshMarkersFromDisk();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
            _seenWriteUtc = Stamp();
        }
        catch (Exception e) { Diagnostics.Warn("settings non sauvegardes: " + e.Message); }
    }

    /// <summary>
    /// Lit le fichier, null s'il n'existe pas. Illisible : on le copie en .bad avant que
    /// le prochain Save ne l'ecrase, pour que l'edition de l'utilisateur ne soit pas perdue.
    /// </summary>
    static Settings? ReadDisk(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(Path_)) return null;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path_), ReadOptions);
            s?.Normalize();
            return s;
        }
        catch (Exception e)
        {
            error = e.Message;
            Diagnostics.Warn("settings illisibles: " + e.Message);
            try { File.Copy(Path_, BadPath, overwrite: true); }
            catch (Exception copy) { Diagnostics.Warn("copie .bad impossible: " + copy.Message); }
            return null;
        }
    }

    static DateTime Stamp()
    {
        // fichier absent : GetLastWriteTimeUtc rend 1601-01-01, pas une exception
        try { return File.GetLastWriteTimeUtc(Path_); }
        catch { return default; }
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
