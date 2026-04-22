namespace dotnet.challenge.data.Cache;

public class SimpleObjectCache<TKey, TValue> : ISimpleObjectCache<TKey, TValue>
    where TKey : struct
    where TValue : class
{
    private readonly Dictionary<TKey, TValue> _store = new();

    public Task AddAsync(TKey key, TValue value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TKey key)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task<TValue?> GetAsync(TKey key)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task<IEnumerable<TValue>> GetAllAsync()
    {
        IEnumerable<TValue> values = _store.Values.ToList();
        return Task.FromResult(values);
    }

    public Task UpdateAsync(TKey key, TValue value)
    {
        if (_store.ContainsKey(key))
        {
            _store[key] = value;
        }
        return Task.CompletedTask;
    }
}
