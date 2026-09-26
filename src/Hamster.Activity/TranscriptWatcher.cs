using System.IO.Enumeration;

namespace Hamster.Activity;

/// <summary>
/// Suit les transcripts de ~/.claude/projects et publie ce qui s'y ajoute, par lots.
/// FileSystemWatcher recursif pour la latence. Filet de securite toutes les 5 s : relecture
/// par handle des fichiers chauds, et, si le FileSystemWatcher a deborde (evenement Error),
/// enumeration complete immediate. L'enumeration complete tourne aussi toutes les 60 s au
/// cas ou il se tairait sans rien dire : mesuree a ~0,6 % d'un coeur si elle tourne toutes
/// les 5 s, chaque appel au systeme de fichiers coutant environ 1 ms a froid sur ce poste.
/// Un seul thread de fond fait toutes les lectures : l'etat par fichier n'a pas besoin de
/// verrou. Events est leve sur ce thread de fond.
/// </summary>
public sealed class TranscriptWatcher : IDisposable
{
    // reglables par les tests
    internal TimeSpan Debounce = TimeSpan.FromMilliseconds(50);
    internal TimeSpan RescanInterval = TimeSpan.FromSeconds(5);
    internal TimeSpan FullRescanInterval = TimeSpan.FromSeconds(60);
    /// <summary>Fichiers ecrits depuis moins que ca : amorces au demarrage (leur fin est relue).</summary>
    internal TimeSpan RecentWindow = TimeSpan.FromMinutes(10);
    /// <summary>Fichiers ecrits depuis moins que ca : relus par handle a chaque controle leger.</summary>
    internal TimeSpan HotWindow = TimeSpan.FromMinutes(2);
    internal int PrimeBytes = 64 * 1024;
    /// <summary>Lecture maximale par fichier et par passe : un gros rattrapage ne bloque pas les autres.</summary>
    internal long MaxBytesPerPass = 8 * 1024 * 1024;
    /// <summary>Faux : filet de securite seul, pour le tester sans que le FileSystemWatcher le masque.</summary>
    internal bool UseFileSystemWatcher = true;

    readonly string _root;
    readonly Dictionary<string, Tracked> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly byte[] _scratch = new byte[64 * 1024];
    readonly object _gate = new();
    HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);
    bool _overflow;
    readonly AutoResetEvent _wake = new(false);
    volatile bool _disposed;
    Thread? _thread;
    FileSystemWatcher? _fsw;
    DateTime _startUtc;
    bool _rootExists;
    volatile string _status = "arrete";

    // compteurs de diagnostic, lus par Status
    long _lines, _events, _safetyReads;

    public TranscriptWatcher(string projectsRoot)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectsRoot));
    }

    public event Action<IReadOnlyList<ActivityEvent>>? Events;

    public string Status => _status;

    public void Start()
    {
        if (_thread != null || _disposed) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hamster.TranscriptWatcher", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _wake.Set(); } catch (ObjectDisposedException) { }
        _thread?.Join(2000);
        DisposeWatcher();
        // _wake n'est pas libere : un rappel du FileSystemWatcher deja en file peut encore le toucher
        _status = "arrete";
    }

    sealed class Tracked(TailFile tail, TranscriptSource? source)
    {
        public readonly TailFile Tail = tail;
        /// <summary>Null : fichier vu mais ignore (journal de workflow, .jsonl inconnu).</summary>
        public TranscriptSource? Source = source;
        /// <summary>Derniere ecriture connue : lecture reussie de notre part, ou date vue a l'enumeration.</summary>
        public DateTime LastWriteUtc;
        public int MetaRetries;
    }

    readonly record struct Entry(string Path, long Length, DateTime CreatedUtc, DateTime WrittenUtc);

    void Run()
    {
        _startUtc = DateTime.UtcNow;
        var batch = new List<ActivityEvent>();
        try
        {
            EnsureWatcher();
            InitialScan(batch);
            UpdateStatus();
            Emit(batch);
        }
        catch (Exception e) { _status = "erreur : " + e.Message; }

        var now = DateTime.UtcNow;
        var nextLight = now + RescanInterval;
        var nextFull = now + FullRescanInterval;
        while (!_disposed)
        {
            try
            {
                var due = nextLight < nextFull ? nextLight : nextFull;
                int wait = (int)Math.Clamp((due - DateTime.UtcNow).TotalMilliseconds, 0, int.MaxValue);
                bool signaled = _wake.WaitOne(wait);
                if (_disposed) break;
                // delai fixe et non glissant : sous un flot continu d'ecritures, un debounce
                // qui se rearme ne publierait jamais rien
                if (signaled && Debounce > TimeSpan.Zero) Thread.Sleep(Debounce);

                HashSet<string> dirty, gone;
                bool overflow;
                lock (_gate)
                {
                    dirty = _dirty; gone = _gone; overflow = _overflow;
                    _dirty = new(StringComparer.OrdinalIgnoreCase);
                    _gone = new(StringComparer.OrdinalIgnoreCase);
                    _overflow = false;
                }
                batch.Clear();
                // supprime ou renomme : s'il reapparait, c'est un autre fichier, suivi comme nouveau
                foreach (var path in gone) _files.Remove(path);

                now = DateTime.UtcNow;
                // dossier absent : on guette son apparition au rythme du controle leger
                bool rootAppeared = !_rootExists && now >= nextLight && Directory.Exists(_root);
                if (overflow || rootAppeared || now >= nextFull)
                {
                    FullRescan(batch);
                    nextFull = DateTime.UtcNow + FullRescanInterval;
                    nextLight = DateTime.UtcNow + RescanInterval;
                }
                else if (now >= nextLight)
                {
                    CheckHot(batch);
                    nextLight = DateTime.UtcNow + RescanInterval;
                }
                foreach (var path in dirty) ProcessPath(path, batch);
                Emit(batch);
                UpdateStatus();
            }
            catch (Exception e)
            {
                // ne doit pas arriver, chaque etape attrape ses erreurs : on le dit, et on continue
                _status = "erreur : " + e.Message;
                nextLight = DateTime.UtcNow + RescanInterval;
            }
        }
    }

    void EnsureWatcher()
    {
        if (_fsw != null || !UseFileSystemWatcher) return;
        if (!Directory.Exists(_root)) return;
        try
        {
            var fsw = new FileSystemWatcher(_root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                // maximum autorise : chaque sous-agent de workflow ecrit en meme temps
                InternalBufferSize = 64 * 1024,
            };
            fsw.Changed += (_, e) => MarkDirty(e.FullPath);
            fsw.Created += (_, e) => MarkDirty(e.FullPath);
            fsw.Deleted += (_, e) => MarkGone(e.FullPath);
            fsw.Renamed += (_, e) => { MarkGone(e.OldFullPath); MarkDirty(e.FullPath); };
            // debordement du tampon : des changements ont ete perdus, on reenumere tout
            fsw.Error += (_, _) => { lock (_gate) _overflow = true; Wake(); };
            fsw.EnableRaisingEvents = true;
            _fsw = fsw;
        }
        catch (Exception e)
        {
            // l'enumeration periodique prend le relais, avec 60 s de latence pour les nouveaux fichiers
            _status = "surveillance degradee : " + e.Message;
        }
    }

    void DisposeWatcher()
    {
        var fsw = _fsw;
        _fsw = null;
        if (fsw == null) return;
        try { fsw.EnableRaisingEvents = false; fsw.Dispose(); } catch { }
    }

    void MarkDirty(string path)
    {
        lock (_gate) _dirty.Add(path);
        Wake();
    }

    void MarkGone(string path)
    {
        lock (_gate) _gone.Add(path);
        Wake();
    }

    void Wake()
    {
        // les rappels du FileSystemWatcher tournent sur le pool de threads : une exception
        // non attrapee y tuerait le processus, meme pendant un Dispose
        if (_disposed) return;
        try { _wake.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Enumere les .jsonl aux seuls endroits ou un transcript peut vivre (slug, dossier de
    /// session, subagents et en dessous) : tool-results, memory, etc. ne sont pas parcourus.
    /// Aucune allocation pour ce qui n'est pas un .jsonl.
    /// </summary>
    List<Entry> Enumerate()
    {
        var list = new List<Entry>(256);
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            int rootLength = _root.Length;
            var files = new FileSystemEnumerable<Entry>(_root,
                (ref FileSystemEntry e) => new Entry(e.ToFullPath(), e.Length, e.CreationTimeUtc.UtcDateTime, e.LastWriteTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    !e.IsDirectory && e.FileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase),
                ShouldRecursePredicate = (ref FileSystemEntry e) =>
                {
                    // profondeur du dossier parent : 0 = racine, 1 = slug, 2 = dossier de session
                    var rel = e.Directory.Length > rootLength ? e.Directory[rootLength..] : ReadOnlySpan<char>.Empty;
                    int depth = rel.Count(Path.DirectorySeparatorChar) + rel.Count(Path.AltDirectorySeparatorChar);
                    return depth switch
                    {
                        0 => true,
                        1 => Guid.TryParse(e.FileName, out _),
                        2 => e.FileName.Equals("subagents", StringComparison.OrdinalIgnoreCase),
                        _ => true,
                    };
                },
            };
            list.AddRange(files);
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>
    /// On saute l'historique, sauf la fin (64 Ko) des fichiers ecrits dans les 10 dernieres
    /// minutes : de quoi savoir qu'une session travaille ou attend deja au lancement.
    /// </summary>
    void InitialScan(List<ActivityEvent> batch)
    {
        _rootExists = Directory.Exists(_root);
        if (!_rootExists) return;
        var now = DateTime.UtcNow;
        foreach (var entry in Enumerate())
        {
            var t = Track(entry, primeFromEnd: true, now);
            if (t?.Source != null) Process(t, batch);
        }
    }

    Tracked? Track(Entry entry, bool primeFromEnd, DateTime now)
    {
        if (_files.TryGetValue(entry.Path, out var known)) return known;
        var source = TranscriptPaths.Classify(entry.Path, _root);
        TailFile tail;
        // apparu depuis notre demarrage : tout son contenu est nouveau
        if (!primeFromEnd && entry.CreatedUtc >= _startUtc - TimeSpan.FromSeconds(2))
            tail = new TailFile(entry.Path, 0);
        else if (now - entry.WrittenUtc <= RecentWindow)
        {
            long start = Math.Max(0, entry.Length - PrimeBytes);
            tail = new TailFile(entry.Path, start, skipToNextLine: start > 0);
        }
        else
            tail = new TailFile(entry.Path, entry.Length);

        var tracked = new Tracked(tail, source) { LastWriteUtc = entry.WrittenUtc };
        _files[entry.Path] = tracked;
        return tracked;
    }

    void ProcessPath(string path, List<ActivityEvent> batch)
    {
        if (!_files.TryGetValue(path, out var t))
        {
            if (!path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return;
            Entry entry;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return;
                entry = new Entry(info.FullName, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
            }
            catch (Exception) { return; }
            t = Track(entry, primeFromEnd: false, DateTime.UtcNow);
            if (t == null) return;
        }
        if (t.Source != null) Process(t, batch);
    }

    /// <summary>
    /// Controle leger : relit par handle les fichiers ecrits recemment. La taille vue par
    /// l'enumeration vient de l'entree de repertoire, que NTFS ne met pas a jour tant que
    /// l'ecrivain garde son handle ouvert, et le FileSystemWatcher ne dit rien non plus.
    /// </summary>
    void CheckHot(List<ActivityEvent> batch)
    {
        var now = DateTime.UtcNow;
        List<Tracked>? hot = null;
        foreach (var t in _files.Values)
            if (t.Source != null && now - t.LastWriteUtc <= HotWindow) (hot ??= new()).Add(t);
        if (hot == null) return;
        foreach (var t in hot)
            if (Process(t, batch)) _safetyReads++;
    }

    void FullRescan(List<ActivityEvent> batch)
    {
        EnsureWatcher();
        _rootExists = Directory.Exists(_root);
        if (!_rootExists)
        {
            _files.Clear();
            DisposeWatcher();
            return;
        }
        var now = DateTime.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Enumerate())
        {
            seen.Add(entry.Path);
            if (!_files.TryGetValue(entry.Path, out var t))
            {
                t = Track(entry, primeFromEnd: false, now);
                if (t?.Source != null && Process(t, batch)) _safetyReads++;
                continue;
            }
            if (t.Source == null) continue;
            if (entry.WrittenUtc > t.LastWriteUtc) t.LastWriteUtc = entry.WrittenUtc;
            // le meta.json d'un sous-agent peut arriver apres son transcript
            if (t.Source.AgentId != null && t.Source.AgentDescription == null && t.MetaRetries < 12)
            {
                t.MetaRetries++;
                var again = TranscriptPaths.Classify(entry.Path, _root);
                if (again?.AgentDescription != null) t.Source = again;
            }
            bool hot = now - t.LastWriteUtc <= HotWindow;
            if ((hot || entry.Length != t.Tail.Offset) && Process(t, batch)) _safetyReads++;
        }
        foreach (var path in _files.Keys.Where(p => !seen.Contains(p)).ToList()) _files.Remove(path);
    }

    /// <summary>Lit la suite d'un fichier. Vrai si des octets ont ete lus.</summary>
    bool Process(Tracked t, List<ActivityEvent> batch)
    {
        var source = t.Source!;
        bool any = false;
        var status = t.Tail.Read(_scratch, MaxBytesPerPass, chunk =>
        {
            any = true;
            t.Tail.FeedLines(chunk, line =>
            {
                _lines++;
                var events = TranscriptParser.ParseLine(line, source);
                if (events.Count > 0) batch.AddRange(events);
            });
        }, out bool more);

        if (status == TailRead.Missing) _files.Remove(t.Tail.Path);
        if (any) t.LastWriteUtc = DateTime.UtcNow;
        // reste a lire : on repasse au prochain tour, apres les autres fichiers
        if (more) MarkDirty(t.Tail.Path);
        return any;
    }

    void Emit(List<ActivityEvent> batch)
    {
        if (batch.Count == 0) return;
        // le meme evenement vu deux fois dans un lot (deux lignes d'un meme message) ne compte qu'une fois
        var seen = new HashSet<ActivityEvent>();
        var unique = new List<ActivityEvent>(batch.Count);
        foreach (var e in batch)
            if (seen.Add(e)) unique.Add(e);
        _events += unique.Count;
        // statut a jour avant les abonnes : ils peuvent le lire des reception du lot
        UpdateStatus();
        try { Events?.Invoke(unique); }
        catch (Exception e) { _status = "erreur dans un abonne : " + e.Message; }
    }

    void UpdateStatus()
    {
        if (!_rootExists)
        {
            _status = "inactif : dossier absent (" + _root + ")";
            return;
        }
        int tracked = 0;
        foreach (var f in _files.Values) if (f.Source != null) tracked++;
        string mode = _fsw != null ? "actif" : "actif (sans FileSystemWatcher)";
        _status = $"{mode} : {tracked} transcripts, {_lines} lignes, {_events} evenements, {_safetyReads} rattrapages";
    }
}
