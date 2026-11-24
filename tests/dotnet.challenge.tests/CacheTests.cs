using dotnet.challenge.data.Cache;

namespace dotnet.challenge.tests;

public class CacheTests
{
    [Fact]
    public async Task Cache_SupportsAddGetUpdateDelete()
    {
        using var cache = new SimpleObjectCache<Guid, string>();
        var key = Guid.NewGuid();

        await cache.AddAsync(key, "first");
        (await cache.GetAsync(key)).Should().Be("first");

        await cache.UpdateAsync(key, "second");
        (await cache.GetAsync(key)).Should().Be("second");

        await cache.DeleteAsync(key);
        (await cache.GetAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_MissingKey_DoesNotInsert()
    {
        using var cache = new SimpleObjectCache<Guid, string>();
        var key = Guid.NewGuid();

        await cache.UpdateAsync(key, "should-not-exist");

        (await cache.GetAsync(key)).Should().BeNull();
        (await cache.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSnapshot()
    {
        using var cache = new SimpleObjectCache<Guid, string>();
        var key = Guid.NewGuid();
        await cache.AddAsync(key, "value");

        var snapshot = (await cache.GetAllAsync()).ToList();
        snapshot.Clear();

        (await cache.GetAllAsync()).Should().ContainSingle().Which.Should().Be("value");
    }

    [Fact]
    public async Task Cache_UnderConcurrentAccess_RemainsConsistent()
    {
        using var cache = new SimpleObjectCache<int, string>();
        const int count = 500;

        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            Task.Run(() => cache.AddAsync(i, i.ToString()))));

        var all = await cache.GetAllAsync();
        all.Should().HaveCount(count);
        all.Should().BeEquivalentTo(Enumerable.Range(0, count).Select(i => i.ToString()));

        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            Task.Run(() => cache.GetAsync(i))));
    }
}
