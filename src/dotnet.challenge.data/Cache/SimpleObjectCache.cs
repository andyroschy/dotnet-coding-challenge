using Microsoft.Extensions.Caching.Memory;

namespace dotnet.challenge.data.Cache;

/// <summary>
/// In-memory cache backed by <see cref="IMemoryCache"/>. Entries never expire:
/// domain lifetimes are owned by the service layer, not by cache eviction.
/// </summary>
public class SimpleObjectCache<TKey, TValue> : ISimpleObjectCache<TKey, TValue>, IDisposable
    where TKey : struct
    where TValue : class
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly object _keysLock = new();
    private readonly HashSet<TKey> _keys = new();

    public Task AddAsync(TKey key, TValue value)
    {
        lock (_keysLock)
        {
            _cache.Set(key, value);
            _keys.Add(key);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(TKey key)
    {
        lock (_keysLock)
        {
            _cache.Remove(key);
            _keys.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<TValue?> GetAsync(TKey key)
    {
        _cache.TryGetValue(key, out TValue? value);
        return Task.FromResult(value);
    }

    public Task<IEnumerable<TValue>> GetAllAsync()
    {
        List<TKey> keys;
        lock (_keysLock)
        {
            keys = _keys.ToList();
        }

        var values = new List<TValue>(keys.Count);
        foreach (var key in keys)
        {
            if (_cache.TryGetValue(key, out TValue? value) && value is not null)
            {
                values.Add(value);
            }
        }

        return Task.FromResult<IEnumerable<TValue>>(values);
    }

    public Task UpdateAsync(TKey key, TValue value)
    {
        lock (_keysLock)
        {
            if (!_keys.Contains(key))
            {
                return Task.CompletedTask;
            }

            _cache.Set(key, value);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
