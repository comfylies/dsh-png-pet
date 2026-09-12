namespace PetHelper;

/// <summary>Small strong-reference cache with deterministic least-recently-used eviction.</summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int capacity;
    private readonly long maximumWeight;
    private readonly Func<TValue, long> weigh;
    private long weight;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recency = [];

    public BoundedLruCache(int capacity, long maximumWeight = long.MaxValue, Func<TValue, long>? weigh = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        this.maximumWeight = maximumWeight;
        this.weigh = weigh ?? (_ => 1);
    }

    public int Count => entries.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        recency.Remove(node);
        recency.AddLast(node);
        value = node.Value.Value;
        return true;
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        if (entries.TryGetValue(key, out var existing))
        {
            weight -= weigh(existing.Value.Value);
            existing.Value = new Entry(key, value);
            weight += weigh(value);
            recency.Remove(existing);
            recency.AddLast(existing);
        }
        else
        {
            var node = recency.AddLast(new Entry(key, value));
            entries.Add(key, node);
            weight += weigh(value);
        }
        while (entries.Count > capacity || weight > maximumWeight)
        {
            var leastRecent = recency.First!;
            weight -= weigh(leastRecent.Value.Value);
            recency.RemoveFirst();
            entries.Remove(leastRecent.Value.Key);
        }
    }

    public void Clear() { entries.Clear(); recency.Clear(); weight = 0; }

    private readonly record struct Entry(TKey Key, TValue Value);
}
