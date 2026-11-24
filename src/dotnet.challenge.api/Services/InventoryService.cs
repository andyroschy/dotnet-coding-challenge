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

    public Task<Product?> GetProductAsync(Guid id) => _products.GetAsync(id);

    public async Task<(ReserveResult Result, Reservation? Reservation)> TryReserveAsync(
        Guid productId, int quantity, int ttlSeconds)
    {
        await _gate.WaitAsync();
        try
        {
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

    public Task<Reservation?> GetReservationAsync(Guid id) => _reservations.GetAsync(id);

    public Task<ConfirmResult> ConfirmAsync(Guid reservationId)
    {
        // TODO: implement idempotent confirmation.
        // - If reservation does not exist: return NotFound.
        // - If already Confirmed: return AlreadyConfirmed (treated as success by the controller).
        // - If Active and not expired: transition to Confirmed and return Success.
        // - Otherwise (Released, Expired): return IllegalState.
        throw new NotImplementedException();
    }

    public Task<ReleaseResult> ReleaseAsync(Guid reservationId)
    {
        // TODO: implement idempotent release.
        // - If reservation does not exist: return NotFound.
        // - If already Released or Expired: return AlreadyReleased (no stock change).
        // - If Active or Confirmed: transition to Released and return the reserved quantity to Product.Stock.
        throw new NotImplementedException();
    }

    public Task SweepExpiredAsync()
    {
        // TODO: find every Active reservation whose ExpiresAt has passed, mark it Expired,
        // and return its quantity to the product's Stock.
        throw new NotImplementedException();
    }

    /// <summary>
    /// True if the reservation's expiry has passed.
    /// </summary>
    public static bool IsExpired(Reservation reservation)
    {
        return reservation.ExpiresAt < DateTime.Now;
    }
}
