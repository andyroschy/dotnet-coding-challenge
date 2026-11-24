using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class ExpiryTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    private static async Task<(InventoryService Service, Product Product, Reservation Reservation)> ReservedAsync(
        int stock, int quantity)
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", stock);
        var (_, reservation) = await svc.TryReserveAsync(product.Id, quantity, ttlSeconds: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        return (svc, product, reservation!);
    }

    [Fact]
    public async Task GetReservation_AfterExpiry_ReportsExpiredAndRestoresStock()
    {
        var (svc, product, reservation) = await ReservedAsync(stock: 5, quantity: 2);

        var observed = await svc.GetReservationAsync(reservation.Id);
        observed!.Status.Should().Be(ReservationStatus.Expired);

        var reloadedProduct = await svc.GetProductAsync(product.Id);
        reloadedProduct!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task GetProduct_AfterExpiry_RestoresStockWithoutExplicitSweep()
    {
        var (svc, product, _) = await ReservedAsync(stock: 5, quantity: 2);

        var reloadedProduct = await svc.GetProductAsync(product.Id);

        reloadedProduct!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task SweepExpired_Repeatedly_RestoresStockExactlyOnce()
    {
        var (svc, product, _) = await ReservedAsync(stock: 5, quantity: 2);

        await svc.SweepExpiredAsync();
        await svc.SweepExpiredAsync();

        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task Reserve_AfterExpiry_CanUseRestoredStock()
    {
        var (svc, product, _) = await ReservedAsync(stock: 1, quantity: 1);

        var (result, _) = await svc.TryReserveAsync(product.Id, 1, ttlSeconds: 60);

        result.Should().Be(ReserveResult.Success);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(0);
    }
}
