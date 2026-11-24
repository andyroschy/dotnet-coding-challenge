using System.Collections.Concurrent;

namespace dotnet.challenge.api.Services;

/// <summary>
/// One semaphore per key, so transitions on unrelated entities never block each other.
/// Entries live as long as the process, matching the in-memory entities they protect.
/// </summary>
internal sealed class KeyedSemaphore<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _semaphores = new();

    public async Task<IDisposable> LockAsync(TKey key)
    {
        var semaphore = _semaphores.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new Lease(semaphore);
    }

    private sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}
