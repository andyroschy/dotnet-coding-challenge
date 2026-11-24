using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class ReservationAtomicityTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    [Fact]
    public async Task ConcurrentReservations_WithLargerQuantity_NeverOversell()
    {
        const int stock = 10;
        const int quantity = 3;
        const int concurrency = 40;

        ThreadPool.SetMinThreads(concurrency + 10, concurrency + 10);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var svc = NewService();
            var product = await svc.CreateProductAsync("Widget", stock);

            var barrier = new Barrier(concurrency);
            var tasks = Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await svc.TryReserveAsync(product.Id, quantity, ttlSeconds: 60);
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            results.Count(r => r.Result == ReserveResult.Success).Should().Be(stock / quantity);
            results.Count(r => r.Result == ReserveResult.InsufficientStock)
                .Should().Be(concurrency - stock / quantity);

            (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(stock % quantity);
        }
    }
}
