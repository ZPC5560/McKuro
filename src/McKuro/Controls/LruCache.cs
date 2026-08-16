namespace McKuro.Controls;

/// <summary>
/// 与 UI 框架无关的固定容量 LRU 缓存(泛型)。
/// <para>
/// 基于 <see cref="LinkedList{T}"/> + <see cref="Dictionary{TKey,TValue}"/> 实现;
/// 所有访问通过 <c>lock</c> 串行化,线程安全。
/// </para>
/// <para>
/// 权重用于按字节/内存上限淘汰:每次 <see cref="Set"/> 传入该项的估算权重,
/// 超出 <see cref="MaxWeight"/> 或 <see cref="MaxEntries"/> 时淘汰最久未使用的项。
/// </para>
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<LruEntry>> _map;
    private readonly LinkedList<LruEntry> _lru = new();
    private long _currentWeight;

    public LruCache(int maxEntries, long maxWeight)
    {
        if (maxEntries < 1) maxEntries = 1;
        if (maxWeight < 0) maxWeight = 0;

        MaxEntries = maxEntries;
        MaxWeight = maxWeight;
        _map = new Dictionary<TKey, LinkedListNode<LruEntry>>();
    }

    /// <summary>最大条目数(<= 0 表示不限制条数,仅按权重淘汰)。</summary>
    public int MaxEntries { get; }

    /// <summary>最大总权重(0 表示不限制权重,仅按条数淘汰)。</summary>
    public long MaxWeight { get; }

    public int Count
    {
        get { lock (_lock) { return _map.Count; } }
    }

    public long CurrentWeight
    {
        get { lock (_lock) { return _currentWeight; } }
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>加入或更新缓存;超出上限时淘汰最久未使用的项。</summary>
    public void Set(TKey key, TValue value, long weight = 0)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _currentWeight -= existing.Value.Weight;
                _map.Remove(key);
            }

            var node = new LinkedListNode<LruEntry>(new LruEntry(key, value, weight));
            _lru.AddFirst(node);
            _map[key] = node;
            _currentWeight += weight;

            EvictIfNeeded();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lru.Clear();
            _map.Clear();
            _currentWeight = 0;
        }
    }

    private void EvictIfNeeded()
    {
        // 优先按权重淘汰,再按条数兜底
        while (MaxWeight > 0 && _currentWeight > MaxWeight && _lru.Last is { } lastNode)
        {
            _lru.RemoveLast();
            _map.Remove(lastNode.Value.Key);
            _currentWeight -= lastNode.Value.Weight;
        }

        while (MaxEntries > 0 && _map.Count > MaxEntries && _lru.Last is { } tailNode)
        {
            _lru.RemoveLast();
            _map.Remove(tailNode.Value.Key);
            _currentWeight -= tailNode.Value.Weight;
        }
    }

    private readonly record struct LruEntry(TKey Key, TValue Value, long Weight);
}
