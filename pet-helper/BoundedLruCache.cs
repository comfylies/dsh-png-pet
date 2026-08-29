namespace PetHelper;

/// <summary>Small strong-reference cache with deterministic least-recently-used eviction.</summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recency = [];

    public BoundedLruCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
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
            existing.Value = new Entry(key, value);
            recency.Remove(existing);
            recency.AddLast(existing);
            return;
        }

        var node = recency.AddLast(new Entry(key, value));
        entries.Add(key, node);
        if (entries.Count <= capacity) return;

        var leastRecent = recency.First!;
        recency.RemoveFirst();
        entries.Remove(leastRecent.Value.Key);
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
