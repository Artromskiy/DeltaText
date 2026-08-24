namespace Delta.Text;

internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly TextCacheBudget _budget;
    private long _bytes;
    private long _clock;

    public BoundedCache(TextCacheBudget budget) => _budget = budget;

    public TValue GetOrAdd(TKey key, Func<TValue> factory, Func<TValue, long> size)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.LastUsed = ++_clock;
                return existing.Value;
            }

            var value = factory();
            var cost = Math.Max(1, size(value));
            if (cost <= _budget.MaxBytes)
            {
                _entries[key] = new Entry(value, cost, ++_clock);
                _bytes += cost;
                Trim();
            }

            return value;
        }
    }

    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
            _bytes = 0;
        }
    }

    private void Trim()
    {
        while (_entries.Count > _budget.MaxEntries || _bytes > _budget.MaxBytes)
        {
            KeyValuePair<TKey, Entry>? oldestPair = null;
            foreach (var pair in _entries)
            {
                if (oldestPair is null || pair.Value.LastUsed < oldestPair.Value.Value.LastUsed)
                {
                    oldestPair = pair;
                }
            }

            if (oldestPair is null)
            {
                return;
            }

            _entries.Remove(oldestPair.Value.Key);
            _bytes -= oldestPair.Value.Value.Bytes;
        }
    }

    private sealed class Entry(TValue value, long bytes, long lastUsed)
    {
        public TValue Value { get; } = value;
        public long Bytes { get; } = bytes;
        public long LastUsed { get; set; } = lastUsed;
    }
}
