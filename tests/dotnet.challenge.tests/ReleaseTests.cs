using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class ReleaseTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    private static async Task<(InventoryService Service, Product Product, Reservation Reservation)> ActiveReservationAsync(
        int stock = 5, int quantity = 2, int ttlSeconds = 60)
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", stock);
        var (_, reservation) = await svc.TryReserveAsync(product.Id, quantity, ttlSeconds);
        return (svc, product, reservation!);
    }

    [Fact]
    public async Task Release_UnknownId_ReturnsNotFound()
    {
        var svc = NewService();

        (await svc.ReleaseAsync(Guid.NewGuid())).Should().Be(ReleaseResult.NotFound);
    }

    [Fact]
    public async Task Release_ActiveReservation_RestoresStockOnceThenIsIdempotent()
    {
        var (svc, product, reservation) = await ActiveReservationAsync();

        (await svc.ReleaseAsync(reservation.Id)).Should().Be(ReleaseResult.Success);
        (await svc.ReleaseAsync(reservation.Id)).Should().Be(ReleaseResult.AlreadyReleased);

        (await svc.GetReservationAsync(reservation.Id))!.Status.Should().Be(ReservationStatus.Released);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task Release_ConfirmedReservation_RestoresStock()
    {
        var (svc, product, reservation) = await ActiveReservationAsync();
        await svc.ConfirmAsync(reservation.Id);

        (await svc.ReleaseAsync(reservation.Id)).Should().Be(ReleaseResult.Success);

        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task Release_ExpiredReservation_IsIdempotentAndDoesNotRestoreTwice()
    {
        var (svc, product, reservation) = await ActiveReservationAsync(ttlSeconds: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        (await svc.ReleaseAsync(reservation.Id)).Should().Be(ReleaseResult.AlreadyReleased);

        (await svc.GetReservationAsync(reservation.Id))!.Status.Should().Be(ReservationStatus.Expired);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task Release_ConcurrentCalls_RestoreStockExactlyOnce()
    {
        var (svc, product, reservation) = await ActiveReservationAsync();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => svc.ReleaseAsync(reservation.Id)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r == ReleaseResult.Success).Should().Be(1);
        results.Count(r => r == ReleaseResult.AlreadyReleased).Should().Be(19);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }
}
