using System.Windows;

namespace PetHelper;

/// <summary>
/// Small bounded cache from a Win32 monitor handle to its already DIP-converted work area.
/// A monitor handle is stable while the monitor stays connected, so the per-flight window
/// physics loop only probes each monitor once instead of issuing MonitorFromPoint /
/// GetMonitorInfo / GetDpiForMonitor P/Invokes on every simulation tick.
/// </summary>
public sealed class MonitorWorkAreaCache
{
    private const int DefaultCapacity = 8;

    private readonly object gate = new();
    private readonly int capacity;
    private readonly List<(nint Monitor, Rect WorkArea)> entries = new();

    public MonitorWorkAreaCache(int capacity = DefaultCapacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public bool TryGet(nint monitor, out Rect workArea)
    {
        lock (gate)
        {
            foreach (var (cachedMonitor, cachedWorkArea) in entries)
            {
                if (cachedMonitor == monitor)
                {
                    workArea = cachedWorkArea;
                    return true;
                }
            }
        }
        workArea = default;
        return false;
    }

    public void Add(nint monitor, Rect workArea)
    {
        lock (gate)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Monitor == monitor)
                {
                    entries[index] = (monitor, workArea);
                    return;
                }
            }

            if (entries.Count >= capacity)
            {
                // A removed monitor can be followed by a brand-new handle. Drop all stale
                // entries instead of guessing which one vanished; probing is cheap.
                entries.Clear();
            }
            entries.Add((monitor, workArea));
        }
    }
}
