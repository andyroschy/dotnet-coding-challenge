using dotnet.challenge.data.Entities;

namespace dotnet.challenge.api.Services;

public enum ReserveResult
{
    Success,
    ProductNotFound,
    InsufficientStock,
    InvalidQuantity
}

public enum ConfirmResult
{
    Success,
    NotFound,
    AlreadyConfirmed,
    IllegalState
}

public enum ReleaseResult
{
    Success,
    NotFound,
    AlreadyReleased,
    IllegalState
}

public enum IncreaseStockResult
{
    Success,
    NotFound,
    InvalidQuantity,
    Overflow
}

public interface IInventoryService
{
    Task<Product> CreateProductAsync(string name, int stock);

    Task<Product?> GetProductAsync(Guid id);

    Task<(IncreaseStockResult Result, Product? Product)> IncreaseStockAsync(Guid productId, int quantity);

    Task<(ReserveResult Result, Reservation? Reservation)> TryReserveAsync(Guid productId, int quantity, int ttlSeconds);

    Task<Reservation?> GetReservationAsync(Guid id);

    Task<ConfirmResult> ConfirmAsync(Guid reservationId);

    Task<ReleaseResult> ReleaseAsync(Guid reservationId);

    Task SweepExpiredAsync();
}
