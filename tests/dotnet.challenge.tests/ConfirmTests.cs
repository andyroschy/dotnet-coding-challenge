using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class ConfirmTests
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
    public async Task Confirm_UnknownId_ReturnsNotFound()
    {
        var svc = NewService();

        (await svc.ConfirmAsync(Guid.NewGuid())).Should().Be(ConfirmResult.NotFound);
    }

    [Fact]
    public async Task Confirm_ActiveReservation_SucceedsThenIsIdempotent()
    {
        var (svc, product, reservation) = await ActiveReservationAsync();

        (await svc.ConfirmAsync(reservation.Id)).Should().Be(ConfirmResult.Success);
        (await svc.ConfirmAsync(reservation.Id)).Should().Be(ConfirmResult.AlreadyConfirmed);

        (await svc.GetReservationAsync(reservation.Id))!.Status.Should().Be(ReservationStatus.Confirmed);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(3);
    }

    [Fact]
    public async Task Confirm_ExpiredReservation_ReturnsIllegalStateAndDoesNotRestoreTwice()
    {
        var (svc, product, reservation) = await ActiveReservationAsync(ttlSeconds: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        (await svc.ConfirmAsync(reservation.Id)).Should().Be(ConfirmResult.IllegalState);

        (await svc.GetReservationAsync(reservation.Id))!.Status.Should().Be(ReservationStatus.Expired);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task Confirm_ReleasedReservation_ReturnsIllegalState()
    {
        var (svc, _, reservation) = await ActiveReservationAsync();
        reservation.Status = ReservationStatus.Released;

        (await svc.ConfirmAsync(reservation.Id)).Should().Be(ConfirmResult.IllegalState);
    }

    [Fact]
    public async Task Confirm_ConcurrentCalls_TransitionExactlyOnce()
    {
        var (svc, product, reservation) = await ActiveReservationAsync();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => svc.ConfirmAsync(reservation.Id)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r == ConfirmResult.Success).Should().Be(1);
        results.Count(r => r == ConfirmResult.AlreadyConfirmed).Should().Be(19);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(3);
    }
}
