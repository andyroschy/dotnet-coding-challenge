using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.api.Services;

public class InventoryService : IInventoryService
{
    private readonly ISimpleObjectCache<Guid, Product> _products;
    private readonly ISimpleObjectCache<Guid, Reservation> _reservations;
    private readonly KeyedSemaphore<Guid> _productLocks = new();
    private readonly KeyedSemaphore<Guid> _reservationLocks = new();

    public InventoryService(
        ISimpleObjectCache<Guid, Product> products,
        ISimpleObjectCache<Guid, Reservation> reservations)
    {
        _products = products;
        _reservations = reservations;
    }

    public async Task<Product> CreateProductAsync(string name, int stock)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stock);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Stock = stock
        };
        await _products.AddAsync(product.Id, product);
        return product;
    }

    public async Task<Product?> GetProductAsync(Guid id)
    {
        await SweepExpiredCoreAsync();
        return await _products.GetAsync(id);
    }

    public async Task<(IncreaseStockResult Result, Product? Product)> IncreaseStockAsync(
        Guid productId, int quantity)
    {
        if (quantity < 1)
        {
            return (IncreaseStockResult.InvalidQuantity, null);
        }

        using var productLock = await _productLocks.LockAsync(productId);
        var product = await _products.GetAsync(productId);
        if (product is null)
        {
            return (IncreaseStockResult.NotFound, null);
        }

        if ((long)product.Stock + quantity > int.MaxValue)
        {
            return (IncreaseStockResult.Overflow, null);
        }

        product.Stock += quantity;
        await _products.UpdateAsync(productId, product);
        return (IncreaseStockResult.Success, product);
    }

    public async Task<(ReserveResult Result, Reservation? Reservation)> TryReserveAsync(
        Guid productId, int quantity, int ttlSeconds)
    {
        if (quantity < 1)
        {
            return (ReserveResult.InvalidQuantity, null);
        }

        await SweepExpiredCoreAsync();

        // Lock order is always reservation, then product. This reservation is new,
        // but taking its lock keeps every write path consistent and deadlock-free.
        var reservationId = Guid.NewGuid();
        using var reservationLock = await _reservationLocks.LockAsync(reservationId);
        using var productLock = await _productLocks.LockAsync(productId);

        var product = await _products.GetAsync(productId);
        if (product is null)
        {
            return (ReserveResult.ProductNotFound, null);
        }

        if (product.Stock < quantity)
        {
            return (ReserveResult.InsufficientStock, null);
        }

        product.Stock -= quantity;
        await _products.UpdateAsync(productId, product);

        var now = DateTime.UtcNow;
        var reservation = new Reservation
        {
            Id = reservationId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(ttlSeconds),
            Status = ReservationStatus.Active
        };
        await _reservations.AddAsync(reservation.Id, reservation);

        return (ReserveResult.Success, reservation);
    }

    public async Task<Reservation?> GetReservationAsync(Guid id)
    {
        var reservation = await _reservations.GetAsync(id);
        if (reservation is null || reservation.Status != ReservationStatus.Active || !IsExpired(reservation))
        {
            return reservation;
        }

        using var reservationLock = await _reservationLocks.LockAsync(id);
        reservation = await _reservations.GetAsync(id);
        if (reservation is not null)
        {
            await ExpireIfNeededAsync(reservation);
        }

        return reservation;
    }

    public async Task<ConfirmResult> ConfirmAsync(Guid reservationId)
    {
        using var reservationLock = await _reservationLocks.LockAsync(reservationId);
        var reservation = await _reservations.GetAsync(reservationId);
        if (reservation is null)
        {
            return ConfirmResult.NotFound;
        }

        await ExpireIfNeededAsync(reservation);

        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return ConfirmResult.AlreadyConfirmed;
        }

        if (reservation.Status != ReservationStatus.Active)
        {
            return ConfirmResult.IllegalState;
        }

        reservation.Status = ReservationStatus.Confirmed;
        await _reservations.UpdateAsync(reservation.Id, reservation);
        return ConfirmResult.Success;
    }

    public async Task<ReleaseResult> ReleaseAsync(Guid reservationId)
    {
        using var reservationLock = await _reservationLocks.LockAsync(reservationId);
        var reservation = await _reservations.GetAsync(reservationId);
        if (reservation is null)
        {
            return ReleaseResult.NotFound;
        }

        await ExpireIfNeededAsync(reservation);

        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return ReleaseResult.IllegalState;
        }

        if (reservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
        {
            return ReleaseResult.AlreadyReleased;
        }

        reservation.Status = ReservationStatus.Released;
        await _reservations.UpdateAsync(reservation.Id, reservation);

        using var productLock = await _productLocks.LockAsync(reservation.ProductId);
        var product = await _products.GetAsync(reservation.ProductId);
        if (product is not null)
        {
            product.Stock += reservation.Quantity;
            await _products.UpdateAsync(product.Id, product);
        }

        return ReleaseResult.Success;
    }

    public Task SweepExpiredAsync() => SweepExpiredCoreAsync();

    /// <summary>
    /// Expires every active reservation whose TTL has elapsed. Never holds a product
    /// lock while acquiring reservation locks: each reservation is locked first.
    /// </summary>
    private async Task SweepExpiredCoreAsync()
    {
        foreach (var reservation in await _reservations.GetAllAsync())
        {
            if (reservation.Status != ReservationStatus.Active || !IsExpired(reservation))
            {
                continue;
            }

            using var reservationLock = await _reservationLocks.LockAsync(reservation.Id);
            await ExpireIfNeededAsync(reservation);
        }
    }

    /// <summary>
    /// Transitions an expired Active reservation to Expired and restores its stock exactly once.
    /// Caller holds the reservation lock.
    /// </summary>
    private async Task ExpireIfNeededAsync(Reservation reservation)
    {
        if (reservation.Status != ReservationStatus.Active || !IsExpired(reservation))
        {
            return;
        }

        reservation.Status = ReservationStatus.Expired;
        await _reservations.UpdateAsync(reservation.Id, reservation);

        using var productLock = await _productLocks.LockAsync(reservation.ProductId);
        var product = await _products.GetAsync(reservation.ProductId);
        if (product is not null)
        {
            product.Stock += reservation.Quantity;
            await _products.UpdateAsync(product.Id, product);
        }
    }

    /// <summary>
    /// True if the reservation's expiry has passed.
    /// </summary>
    public static bool IsExpired(Reservation reservation)
    {
        return reservation.ExpiresAt <= DateTime.UtcNow;
    }
}
