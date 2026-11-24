using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class FineGrainedLockingTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    [Fact]
    public async Task Reserve_DifferentProducts_DoNotBlockEachOther()
    {
        var products = new BlockingCache<Guid, Product>();
        var svc = new InventoryService(products, new SimpleObjectCache<Guid, Reservation>());
        var blockedProduct = await svc.CreateProductAsync("Blocked", 5);
        var otherProduct = await svc.CreateProductAsync("Other", 5);
        products.BlockedKey = blockedProduct.Id;

        var slow = svc.TryReserveAsync(blockedProduct.Id, 1, ttlSeconds: 60);
        await products.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var fast = await svc.TryReserveAsync(otherProduct.Id, 1, ttlSeconds: 60)
            .WaitAsync(TimeSpan.FromSeconds(2));
        fast.Result.Should().Be(ReserveResult.Success);

        products.Proceed.TrySetResult(true);
        (await slow).Result.Should().Be(ReserveResult.Success);
    }

    [Fact]
    public async Task Release_DifferentReservations_DoNotBlockEachOther()
    {
        var reservations = new BlockingCache<Guid, Reservation>();
        var svc = new InventoryService(new SimpleObjectCache<Guid, Product>(), reservations);
        var product = await svc.CreateProductAsync("Widget", 2);
        var (_, blocked) = await svc.TryReserveAsync(product.Id, 1, ttlSeconds: 60);
        var (_, other) = await svc.TryReserveAsync(product.Id, 1, ttlSeconds: 60);
        reservations.BlockedUpdateKey = blocked!.Id;

        var slow = svc.ReleaseAsync(blocked.Id);
        await reservations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var observed = await svc.GetReservationAsync(blocked.Id).WaitAsync(TimeSpan.FromSeconds(2));
        observed.Should().NotBeNull();

        var fast = await svc.ReleaseAsync(other!.Id).WaitAsync(TimeSpan.FromSeconds(2));
        fast.Should().Be(ReleaseResult.Success);

        reservations.Proceed.TrySetResult(true);
        (await slow).Should().Be(ReleaseResult.Success);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentReleaseAndSweep_CompleteWithoutDeadlock()
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", 5);
        var (_, reservation) = await svc.TryReserveAsync(product.Id, 2, ttlSeconds: 60);

        var releases = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => svc.ReleaseAsync(reservation!.Id)))
            .ToArray();
        var sweeps = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => svc.SweepExpiredAsync()))
            .ToArray();

        await Task.WhenAll(releases.Cast<Task>().Concat(sweeps))
            .WaitAsync(TimeSpan.FromSeconds(10));

        releases.Count(t => t.Result == ReleaseResult.Success).Should().Be(1);
        releases.Count(t => t.Result == ReleaseResult.AlreadyReleased).Should().Be(7);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    private sealed class BlockingCache<TKey, TValue> : ISimpleObjectCache<TKey, TValue>
        where TKey : struct
        where TValue : class
    {
        private readonly ISimpleObjectCache<TKey, TValue> _inner = new SimpleObjectCache<TKey, TValue>();

        public TKey? BlockedKey { get; set; }
        public TKey? BlockedUpdateKey { get; set; }
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Proceed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TValue?> GetAsync(TKey key)
        {
            if (BlockedKey.HasValue && EqualityComparer<TKey>.Default.Equals(key, BlockedKey.Value))
            {
                Entered.TrySetResult(true);
                await Proceed.Task;
            }

            return await _inner.GetAsync(key);
        }

        public Task AddAsync(TKey key, TValue value) => _inner.AddAsync(key, value);

        public Task DeleteAsync(TKey key) => _inner.DeleteAsync(key);

        public Task<IEnumerable<TValue>> GetAllAsync() => _inner.GetAllAsync();

        public async Task UpdateAsync(TKey key, TValue value)
        {
            if (BlockedUpdateKey.HasValue && EqualityComparer<TKey>.Default.Equals(key, BlockedUpdateKey.Value))
            {
                Entered.TrySetResult(true);
                await Proceed.Task;
            }

            await _inner.UpdateAsync(key, value);
        }
    }
}
