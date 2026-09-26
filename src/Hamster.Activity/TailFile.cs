namespace Hamster.Activity;

internal enum TailRead { Missing, Unchanged, Read, Busy }

/// <summary>
/// Suit un fichier qui grandit par la fin. Aucun handle n'est garde entre deux lectures, et
/// l'ouverture partage lecture, ecriture et suppression : Claude Code, ou le hook, peut
/// ecrire, renommer ou effacer le fichier a tout moment sans jamais tomber sur un verrou.
/// Les octets deja lus mais pas encore consommes (ligne ou valeur JSON incomplete)
/// restent en tampon jusqu'a la lecture suivante.
/// </summary>
internal sealed class TailFile
{
    /// <summary>Au-dela, une "ligne" sans fin n'est plus une ligne : on la jette.</summary>
    internal const int MaxPending = 64 * 1024 * 1024;

    public readonly string Path;
    /// <summary>Position dans le fichier jusqu'ou tout a ete lu (tampon compris).</summary>
    public long Offset;
    /// <summary>Jeter tout jusqu'au prochain saut de ligne : lecture commencee au milieu d'une ligne.</summary>
    public bool SkipToNextLine;

    byte[]? _pending;
    int _pendingLength;
    bool _discarding;

    public TailFile(string path, long offset, bool skipToNextLine = false)
    {
        Path = path;
        Offset = offset;
        SkipToNextLine = skipToNextLine;
    }

    public int PendingLength => _pendingLength;
    public bool Truncated { get; private set; }

    public void Reset(long offset = 0, bool skipToNextLine = false)
    {
        Offset = offset;
        SkipToNextLine = skipToNextLine;
        _pending = null;
        _pendingLength = 0;
        _discarding = false;
    }

    /// <summary>
    /// Lit au plus maxBytes nouveaux octets. Un fichier plus court que l'offset a ete tronque
    /// ou remplace : on repart de zero. more : il reste des octets a lire.
    /// </summary>
    public TailRead Read(byte[] scratch, long maxBytes, Action<ReadOnlyMemory<byte>> consume, out bool more)
    {
        more = false;
        Truncated = false;
        FileStream fs;
        try
        {
            fs = new FileStream(Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return TailRead.Missing; }
        catch (UnauthorizedAccessException) { return TailRead.Missing; }
        catch (IOException) { return TailRead.Busy; }

        using (fs)
        {
            long length;
            try { length = fs.Length; }
            catch (IOException) { return TailRead.Busy; }

            if (length < Offset)
            {
                Reset();
                Truncated = true;
            }
            if (length == Offset) return TailRead.Unchanged;

            long toRead = Math.Min(length - Offset, maxBytes);
            fs.Position = Offset;
            bool atStart = Offset == 0;
            while (toRead > 0)
            {
                int n;
                try { n = fs.Read(scratch, 0, (int)Math.Min(scratch.Length, toRead)); }
                catch (IOException) { break; }
                if (n <= 0) break;
                toRead -= n;
                Offset += n;
                int skip = 0;
                // BOM UTF-8 eventuel en tete de fichier
                if (atStart && n >= 3 && scratch[0] == 0xEF && scratch[1] == 0xBB && scratch[2] == 0xBF) skip = 3;
                atStart = false;
                consume(scratch.AsMemory(skip, n - skip));
            }
            more = Offset < length;
            return TailRead.Read;
        }
    }

    /// <summary>
    /// Decoupe en lignes (sans le \r final) ce qui vient d'etre lu. Les lignes vides sont sautees.
    /// Les ReadOnlyMemory passees a onLine ne vivent que le temps de l'appel.
    /// </summary>
    public void FeedLines(ReadOnlyMemory<byte> chunk, Action<ReadOnlyMemory<byte>> onLine)
    {
        var span = chunk.Span;
        int start = 0;
        if (SkipToNextLine || _discarding)
        {
            int nl = span.IndexOf((byte)'\n');
            if (nl < 0) return;
            SkipToNextLine = false;
            _discarding = false;
            start = nl + 1;
        }
        if (_pendingLength > 0)
        {
            int nl = span[start..].IndexOf((byte)'\n');
            if (nl < 0)
            {
                Append(span[start..]);
                return;
            }
            Append(span.Slice(start, nl));
            if (!_discarding) EmitLine(_pending.AsMemory(0, _pendingLength), onLine);
            _pending = null;
            _pendingLength = 0;
            _discarding = false;
            start += nl + 1;
        }
        while (start < span.Length)
        {
            int nl = span[start..].IndexOf((byte)'\n');
            if (nl < 0) break;
            EmitLine(chunk.Slice(start, nl), onLine);
            start += nl + 1;
        }
        if (start < span.Length) Append(span[start..]);
    }

    /// <summary>
    /// Pour les lecteurs qui decoupent eux-memes (valeurs JSON du hook) : tampon complet,
    /// puis Consume du nombre d'octets exploites.
    /// </summary>
    public ReadOnlyMemory<byte> Pending => _pending is null ? ReadOnlyMemory<byte>.Empty : _pending.AsMemory(0, _pendingLength);

    public void AppendRaw(ReadOnlySpan<byte> data) => Append(data);

    public void Consume(int count)
    {
        if (count <= 0 || _pending is null) return;
        if (count >= _pendingLength)
        {
            _pending = null;
            _pendingLength = 0;
            return;
        }
        _pending.AsSpan(count, _pendingLength - count).CopyTo(_pending);
        _pendingLength -= count;
    }

    void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _discarding) return;
        if (_pendingLength + data.Length > MaxPending)
        {
            // ligne demesuree : on la laisse tomber jusqu'au prochain saut de ligne
            _pending = null;
            _pendingLength = 0;
            _discarding = true;
            return;
        }
        if (_pending is null || _pending.Length < _pendingLength + data.Length)
        {
            var grown = new byte[Math.Max(_pendingLength + data.Length, Math.Min(MaxPending, Math.Max(4096, (_pending?.Length ?? 0) * 2)))];
            if (_pending is not null) _pending.AsSpan(0, _pendingLength).CopyTo(grown);
            _pending = grown;
        }
        data.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += data.Length;
    }

    static void EmitLine(ReadOnlyMemory<byte> line, Action<ReadOnlyMemory<byte>> onLine)
    {
        if (line.Length > 0 && line.Span[^1] == (byte)'\r') line = line[..^1];
        if (line.Length > 0) onLine(line);
    }
}
