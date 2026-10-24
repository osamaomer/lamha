namespace Lamha.Core.Caching;

/// <summary>A small cache that forgets the least recently used entry when full: Lamha 2's <c>LRU</c>. Thread-safe.</summary>
public sealed class Lru<TKey, TValue>(int capacity)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly Lock _gate = new();

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.Remove(key, out var old))
            {
                _order.Remove(old);
            }

            _map[key] = _order.AddFirst((key, value));
            if (_map.Count > capacity)
            {
                _map.Remove(_order.Last!.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
