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

    /// <summary>Vrai si le fichier illisible a bien ete copie en settings.json.bad.</summary>
    [JsonIgnore]
    public bool LoadBackedUp { get; private set; }

    /// <summary>
    /// Leve quand un settings.json edite a la main se revele illisible en cours de route :
    /// message d'erreur, et vrai si la copie .bad a reussi.
    /// </summary>
    public event Action<string, bool>? Unreadable;

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

    // date d'ecriture du fichier a la derniere lecture reussie ou ecriture de notre part
    DateTime _seenWriteUtc;

    /// <summary>
    /// Resultat d'une lecture. Value null + Error null = fichier absent.
    /// Busy = fichier tenu par un autre processus (un editeur qui enregistre) : a retenter,
    /// ce n'est pas un fichier casse.
    /// </summary>
    readonly record struct DiskRead(Settings? Value, string? Error, bool BackedUp, bool Busy);

    public static Settings Load()
    {
        // au demarrage on insiste plus : repartir des valeurs par defaut ferait ecraser
        // position et reglages au premier Save
        var stamp = Stamp();
        var read = ReadDisk(attempts: 10);
        var s = read.Value ?? new Settings();
        s.LoadError = read.Error;
        s.LoadBackedUp = read.BackedUp;
        s._seenWriteUtc = read.Busy ? default : stamp;
        return s;
    }

    /// <summary>
    /// Reprend les marqueurs du disque si le fichier a change depuis notre derniere lecture
    /// ou ecriture : l'instance lancee a l'ouverture de session tourne deja quand on edite
    /// le fichier. Vrai si la liste a change.
    /// </summary>
    public bool RefreshMarkersFromDisk()
    {
        // date lue avant le contenu : une ecriture entre les deux donnera une date
        // differente au prochain passage, donc une relecture de plus, jamais de moins
        var stamp = Stamp();
        if (stamp == _seenWriteUtc) return false;

        var read = ReadDisk(attempts: 3);
        // occupe : on ne marque pas la date comme vue, le prochain passage retentera
        if (read.Busy) return false;
        _seenWriteUtc = stamp;

        if (read.Error != null)
        {
            LoadError = read.Error;
            LoadBackedUp = read.BackedUp;
            Unreadable?.Invoke(read.Error, read.BackedUp);
            return false;
        }
        var disk = read.Value;
        if (disk == null || disk.ClaudeDesktopPathMarkers.SequenceEqual(ClaudeDesktopPathMarkers)) return false;

        ClaudeDesktopPathMarkers = disk.ClaudeDesktopPathMarkers;
        Diagnostics.Info("marqueurs Claude Desktop relus: " + string.Join(", ", ClaudeDesktopPathMarkers));
        return true;
    }

    public void Save()
    {
        // une edition a la main pas encore relue serait ecrasee par la version en memoire
        RefreshMarkersFromDisk();
        if (Stamp() != _seenWriteUtc)
        {
            // encore tenu par un editeur : on garde le reglage en memoire, le prochain
            // Save l'ecrira, plutot que d'ecraser une edition qu'on n'a pas pu lire
            Diagnostics.Warn("settings occupes, sauvegarde reportee");
            return;
        }
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
    /// Lit le fichier. Illisible (JSON casse) : on le copie en .bad avant que le prochain
    /// Save ne l'ecrase, pour que l'edition de l'utilisateur ne soit pas perdue.
    /// </summary>
    static DiskRead ReadDisk(int attempts)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(Path_)) return default;
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path_), ReadOptions);
                s?.Normalize();
                return new DiskRead(s, null, false, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // un editeur qui enregistre garde le fichier ouvert en ecriture un instant
                if (attempt < attempts) { Thread.Sleep(100); continue; }
                Diagnostics.Warn("settings occupes, relecture plus tard: " + e.Message);
                return new DiskRead(null, null, false, true);
            }
            catch (Exception e)
            {
                Diagnostics.Warn("settings illisibles: " + e.Message);
                bool backedUp = false;
                try { File.Copy(Path_, BadPath, overwrite: true); backedUp = true; }
                catch (Exception copy) { Diagnostics.Warn("copie .bad impossible: " + copy.Message); }
                return new DiskRead(null, e.Message, backedUp, false);
            }
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
