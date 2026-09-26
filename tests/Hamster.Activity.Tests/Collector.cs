using System.Diagnostics;

namespace Hamster.Activity.Tests;

/// <summary>Recoit les lots d'un watcher (thread de fond) et permet de les attendre.</summary>
sealed class Collector
{
    readonly object _gate = new();
    readonly List<(long Ticks, ActivityEvent Event)> _events = new();
    public int Batches;

    public void Add(IReadOnlyList<ActivityEvent> batch)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            Batches++;
            foreach (var e in batch) _events.Add((now, e));
            Monitor.PulseAll(_gate);
        }
    }

    public List<ActivityEvent> All()
    {
        lock (_gate) return _events.Select(x => x.Event).ToList();
    }

    public int Count
    {
        get { lock (_gate) return _events.Count; }
    }

    /// <summary>Attend un evenement qui satisfait match ; rend l'instant de sa reception (Stopwatch), ou null.</summary>
    public long? WaitFor(Func<ActivityEvent, bool> match, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        lock (_gate)
        {
            while (true)
            {
                foreach (var (ticks, e) in _events)
                    if (match(e)) return ticks;
                long left = deadline - Stopwatch.GetTimestamp();
                if (left <= 0) return null;
                Monitor.Wait(_gate, TimeSpan.FromSeconds((double)left / Stopwatch.Frequency));
            }
        }
    }

    public static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
}
