using dotnet.challenge.api.Services;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests;

public class ExpiryClockTests
{
    [Fact]
    public void IsExpired_WithUtcTimestamps_UsesUtcClock()
    {
        var expired = new Reservation { ExpiresAt = DateTime.UtcNow.AddSeconds(-1) };
        var active = new Reservation { ExpiresAt = DateTime.UtcNow.AddMinutes(1) };

        InventoryService.IsExpired(expired).Should().BeTrue();
        InventoryService.IsExpired(active).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AtExactExpiry_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var reservation = new Reservation { ExpiresAt = now };

        InventoryService.IsExpired(reservation).Should().BeTrue();
    }

    [Fact]
    public async Task Reserve_CreatedAndExpiryTimes_AreUtcAndTtlApart()
    {
        var svc = new InventoryService(
            new dotnet.challenge.data.Cache.SimpleObjectCache<Guid, Product>(),
            new dotnet.challenge.data.Cache.SimpleObjectCache<Guid, Reservation>());
        var product = await svc.CreateProductAsync("Widget", 1);

        var (_, reservation) = await svc.TryReserveAsync(product.Id, 1, ttlSeconds: 30);

        reservation!.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        reservation.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
        (reservation.ExpiresAt - reservation.CreatedAt).Should().Be(TimeSpan.FromSeconds(30));
    }
}
