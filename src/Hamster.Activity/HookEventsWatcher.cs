using System.Text.Json;

namespace Hamster.Activity;

/// <summary>
/// Suit ~/.hamster/events.jsonl, ou Hooks/hook.sh recopie le JSON que Claude Code passe a
/// son hook Notification. Seules les demandes d'autorisation et de saisie comptent : c'est
/// le seul moyen de les voir, elles n'apparaissent pas dans les transcripts.
/// Le hook n'ecrit pas forcement une valeur par ligne (JSON sur plusieurs lignes, deux hooks
/// entrelaces) : le fichier se lit comme une suite de valeurs JSON separees par des blancs.
/// Events est leve sur un thread de fond.
/// </summary>
public sealed class HookEventsWatcher : IDisposable
{
    /// <summary>
    /// Types de notification qui veulent dire "l'utilisateur doit agir". idle_prompt n'en est pas.
    /// worker_permission_prompt : absent de la doc des hooks, sa source est le binaire de
    /// Claude Code 2.1.283 (liste des types de notification, et InboxPoller du mode equipe :
    /// "... needs permission for ...", "... needs network access to ..."). Traite comme
    /// permission_prompt ; Detail garde le type recu.
    /// </summary>
    internal static readonly HashSet<string> NeedsUserTypes = new(StringComparer.Ordinal)
    {
        "permission_prompt", "worker_permission_prompt", "agent_needs_input", "elicitation_dialog", "elicitation_url_dialog",
    };

    // reglables par les tests
    internal TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
    internal TimeSpan RescanInterval = TimeSpan.FromSeconds(5);
    internal long RotateBytes = 5 * 1024 * 1024;
    /// <summary>La charge du hook n'a pas d'horodatage : l'evenement date de sa lecture.</summary>
    internal Func<DateTimeOffset> Clock = () => DateTimeOffset.UtcNow;

    static readonly JsonReaderOptions ReaderOptions = new() { AllowMultipleValues = true, MaxDepth = 256 };

    readonly string _path;
    readonly string _directory;
    readonly string _fileName;
    readonly byte[] _scratch = new byte[64 * 1024];
    readonly AutoResetEvent _wake = new(false);
    readonly object _gate = new();
    DateTime _replacedUtc;
    volatile bool _disposed;
    Thread? _thread;
    FileSystemWatcher? _fsw;
    TailFile? _tail;
    bool _exists, _directoryExists;
    volatile bool _watcherBroken;
    DateTime _tailSinceUtc, _rotatedUtc;
    volatile string _status = "arrete";
    long _values, _events, _garbage, _ignored;

    public HookEventsWatcher(string eventsPath)
    {
        _path = Path.GetFullPath(eventsPath);
        _directory = Path.GetDirectoryName(_path)!;
        _fileName = Path.GetFileName(_path);
    }

    public static string DefaultEventsPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("HAMSTER_EVENTS_PATH");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hamster", "events.jsonl");
        }
    }

    public event Action<IReadOnlyList<ActivityEvent>>? Events;

    /// <summary>
    /// Optionnel, a fixer avant Start : vrai si un transcript a deja donne un evenement pour
    /// cette session (ActivityModel.KnowsSession). Une demande pour une autre session est
    /// ignoree et comptee dans Status : rien dans les transcripts ne leverait son attente.
    /// Appele sur le thread de fond du watcher. Null : tout passe, le modele filtre lui-meme.
    /// </summary>
    public Func<string, bool>? KnownSession { get; set; }

    public string Status => _status;

    public void Start()
    {
        if (_thread != null || _disposed) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hamster.HookEventsWatcher", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _wake.Set(); } catch (ObjectDisposedException) { }
        _thread?.Join(2000);
        DisposeWatcher();
        _status = "arrete";
    }

    void Run()
    {
        // on saute l'historique : les notifications d'avant le lancement ne disent plus rien
        try
        {
            var info = new FileInfo(_path);
            _exists = info.Exists;
            if (_exists) Follow(info.Length);
        }
        catch (Exception) { _tail = null; }
        EnsureWatcher();
        UpdateStatus();

        var batch = new List<ActivityEvent>();
        var nextRescan = DateTime.UtcNow + RescanInterval;
        while (!_disposed)
        {
            try
            {
                int wait = (int)Math.Clamp((nextRescan - DateTime.UtcNow).TotalMilliseconds, 0, int.MaxValue);
                bool signaled = _wake.WaitOne(wait);
                if (_disposed) break;
                if (signaled && Debounce > TimeSpan.Zero) Thread.Sleep(Debounce);
                if (DateTime.UtcNow >= nextRescan || _watcherBroken)
                {
                    EnsureWatcher();
                    nextRescan = DateTime.UtcNow + RescanInterval;
                }
                DateTime replaced;
                lock (_gate) replaced = _replacedUtc;
                // supprime ou renomme apres qu'on a commence a le suivre : le fichier qui porte ce
                // nom maintenant est un autre, a lire depuis le debut. Un avis plus ancien que notre
                // suivi (notre propre rotation, vue en retard) ne doit pas faire tout relire :
                // chaque notification reviendrait comme une demande neuve
                if (_tail != null && replaced > _tailSinceUtc && replaced > _rotatedUtc + TimeSpan.FromSeconds(1)) _tail = null;

                batch.Clear();
                Pump(batch);
                if (batch.Count > 0)
                {
                    _events += batch.Count;
                    // statut a jour avant les abonnes : ils peuvent le lire des reception du lot
                    UpdateStatus();
                    try { Events?.Invoke(batch.ToArray()); }
                    catch (Exception e) { _status = "erreur dans un abonne : " + e.Message; }
                }
                else UpdateStatus();
            }
            catch (Exception e)
            {
                _status = "erreur : " + e.Message;
                nextRescan = DateTime.UtcNow + RescanInterval;
            }
        }
    }

    /// <summary>
    /// Un seul appel au systeme de fichiers quand rien n'a change (l'ouverture) : chacun coute
    /// de l'ordre d'une milliseconde a froid, et cette boucle tourne toute la journee.
    /// </summary>
    void Pump(List<ActivityEvent> batch)
    {
        if (_tail == null)
        {
            // absent : on le constate sans ouvrir, car l'exception d'une ouverture ratee coute
            // a elle seule plusieurs millisecondes, toutes les 5 s
            if ((_fsw == null && !_directoryExists) || !File.Exists(_path))
            {
                _exists = false;
                return;
            }
        }
        // absent jusqu'ici, cree depuis (ou recree apres rotation) : tout est nouveau
        var tail = _tail ?? new TailFile(_path, 0);
        for (int pass = 0; pass < 64; pass++)
        {
            var read = tail.Read(_scratch, 4 * 1024 * 1024, chunk =>
            {
                tail.AppendRaw(chunk.Span);
                tail.Consume(ParseValues(tail.Pending, batch));
            }, out bool more);
            if (read == TailRead.Missing)
            {
                _tail = null;
                _exists = false;
                return;
            }
            if (read == TailRead.Busy) return;
            _exists = true;
            if (_tail != tail)
            {
                _tail = tail;
                _tailSinceUtc = DateTime.UtcNow;
            }
            if (!more) break;
        }
        if (tail.Offset > RotateBytes) Rotate(batch);
    }

    void Follow(long offset)
    {
        _tail = new TailFile(_path, offset);
        _tailSinceUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Au-dela de 5 Mo : renomme en events.jsonl.1, le prochain hook recree le fichier.
    /// Tenu par un hook en train d'ecrire, le renommage echoue et sera retente.
    /// </summary>
    void Rotate(List<ActivityEvent> batch)
    {
        var tail = _tail!;
        string old = _path + ".1";
        try { File.Move(_path, old, overwrite: true); }
        catch (Exception) { return; }
        _rotatedUtc = DateTime.UtcNow;
        // une ecriture glissee entre notre lecture et le renommage est partie dans le .1
        var rest = new TailFile(old, tail.Offset);
        rest.AppendRaw(tail.Pending.Span);
        rest.Read(_scratch, long.MaxValue, chunk =>
        {
            rest.AppendRaw(chunk.Span);
            rest.Consume(ParseValues(rest.Pending, batch));
        }, out _);
        _tail = null;
    }

    /// <summary>Decode les valeurs completes du tampon. Rend le nombre d'octets exploites.</summary>
    internal int ParseValues(ReadOnlyMemory<byte> data, List<ActivityEvent> batch)
    {
        int consumed = 0;
        while (consumed < data.Length)
        {
            var slice = data.Span[consumed..];
            var reader = new Utf8JsonReader(slice, isFinalBlock: false, new JsonReaderState(ReaderOptions));
            int used;
            try
            {
                if (!JsonDocument.TryParseValue(ref reader, out var doc)) break;
                used = (int)reader.BytesConsumed;
                using (doc)
                {
                    _values++;
                    if (ToEvent(doc.RootElement, Clock()) is { } e)
                    {
                        if (KnownSession is { } known && !Known(known, e.SessionId)) _ignored++;
                        else batch.Add(e);
                    }
                }
            }
            catch (JsonException)
            {
                // valeur illisible (ecriture tronquee, hooks entrelaces au milieu d'une valeur) :
                // on la saute jusqu'a la fin de ligne, et on reprend
                int nl = slice.IndexOf((byte)'\n');
                if (nl < 0) break;
                _garbage++;
                used = nl + 1;
            }
            if (used <= 0) break;
            consumed += used;
        }
        return consumed;
    }

    /// <summary>
    /// Notification que l'utilisateur doit traiter => NeedsUser, Detail = notification_type.
    /// agent_id n'est pas envoye par la version observee (2.1.283) pour Notification ; s'il
    /// apparait, il designe le fil qui attend.
    /// </summary>
    internal static ActivityEvent? ToEvent(JsonElement root, DateTimeOffset time)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Str(root, "hook_event_name") != "Notification") return null;
        string? type = Str(root, "notification_type");
        if (type == null || !NeedsUserTypes.Contains(type)) return null;
        string? session = Str(root, "session_id");
        if (string.IsNullOrEmpty(session)) return null;
        return new ActivityEvent(time, ActivityKind.NeedsUser, session, Str(root, "agent_id"), Str(root, "cwd"), null, false, type);
    }

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Le filtre vient de l'appelant : s'il leve, la demande passe, le modele tranchera.</summary>
    static bool Known(Func<string, bool> known, string session)
    {
        try { return known(session); }
        catch (Exception) { return true; }
    }

    void EnsureWatcher()
    {
        if (_fsw != null && !_watcherBroken) return;
        DisposeWatcher();
        _watcherBroken = false;
        _directoryExists = Directory.Exists(_directory);
        if (!_directoryExists) return;
        try
        {
            var fsw = new FileSystemWatcher(_directory, _fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            fsw.Changed += (_, _) => Wake();
            fsw.Created += (_, _) => Wake();
            fsw.Deleted += (_, _) => Replaced();
            fsw.Renamed += (_, _) => Replaced();
            // tampon deborde ou dossier supprime : on recree la surveillance au prochain tour
            fsw.Error += (_, _) => { _watcherBroken = true; Wake(); };
            fsw.EnableRaisingEvents = true;
            _fsw = fsw;
        }
        catch (Exception e) { _status = "surveillance degradee : " + e.Message; }
    }

    void DisposeWatcher()
    {
        var fsw = _fsw;
        _fsw = null;
        if (fsw == null) return;
        try { fsw.EnableRaisingEvents = false; fsw.Dispose(); } catch { }
    }

    void Replaced()
    {
        lock (_gate) _replacedUtc = DateTime.UtcNow;
        Wake();
    }

    void Wake()
    {
        if (_disposed) return;
        try { _wake.Set(); } catch (ObjectDisposedException) { }
    }

    void UpdateStatus()
    {
        if (!_exists)
        {
            _status = "inactif : " + _path + " absent";
            return;
        }
        _status = $"actif : {_values} notifications lues, {_events} demandes, {_garbage} illisibles, {_ignored} ignorees";
    }
}
