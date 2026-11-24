using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.api.Services;

public class InventoryService : IInventoryService
{
    private readonly ISimpleObjectCache<Guid, Product> _products;
    private readonly ISimpleObjectCache<Guid, Reservation> _reservations;

    // The cache is thread-safe, but a compound transition is not. This gate
    // serializes each complete state change for the single API instance.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InventoryService(
        ISimpleObjectCache<Guid, Product> products,
        ISimpleObjectCache<Guid, Reservation> reservations)
    {
        _products = products;
        _reservations = reservations;
    }

    public async Task<Product> CreateProductAsync(string name, int stock)
    {
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
        await _gate.WaitAsync();
        try
        {
            await SweepExpiredCoreAsync();
            return await _products.GetAsync(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(ReserveResult Result, Reservation? Reservation)> TryReserveAsync(
        Guid productId, int quantity, int ttlSeconds)
    {
        await _gate.WaitAsync();
        try
        {
            await SweepExpiredCoreAsync();

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
                Id = Guid.NewGuid(),
                ProductId = productId,
                Quantity = quantity,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(ttlSeconds),
                Status = ReservationStatus.Active
            };
            await _reservations.AddAsync(reservation.Id, reservation);

            return (ReserveResult.Success, reservation);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Reservation?> GetReservationAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var reservation = await _reservations.GetAsync(id);
            if (reservation is not null)
            {
                await ExpireIfNeededAsync(reservation);
            }

            return reservation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfirmResult> ConfirmAsync(Guid reservationId)
    {
        await _gate.WaitAsync();
        try
        {
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
        finally
        {
            _gate.Release();
        }
    }

    public Task<ReleaseResult> ReleaseAsync(Guid reservationId)
    {
        // TODO: implement idempotent release.
        // - If reservation does not exist: return NotFound.
        // - If already Released or Expired: return AlreadyReleased (no stock change).
        // - If Active or Confirmed: transition to Released and return the reserved quantity to Product.Stock.
        throw new NotImplementedException();
    }

    public async Task SweepExpiredAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await SweepExpiredCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Expires every active reservation whose TTL has elapsed. Caller holds <see cref="_gate"/>.
    /// </summary>
    private async Task SweepExpiredCoreAsync()
    {
        foreach (var reservation in await _reservations.GetAllAsync())
        {
            await ExpireIfNeededAsync(reservation);
        }
    }

    /// <summary>
    /// Transitions an expired Active reservation to Expired and restores its stock exactly once.
    /// Caller holds <see cref="_gate"/>.
    /// </summary>
    private async Task ExpireIfNeededAsync(Reservation reservation)
    {
        if (reservation.Status != ReservationStatus.Active || !IsExpired(reservation))
        {
            return;
        }

        reservation.Status = ReservationStatus.Expired;
        await _reservations.UpdateAsync(reservation.Id, reservation);

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
