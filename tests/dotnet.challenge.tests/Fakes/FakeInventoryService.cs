using dotnet.challenge.api.Services;
using dotnet.challenge.data.Entities;

namespace dotnet.challenge.tests.Fakes;

public sealed class FakeInventoryService : IInventoryService
{
    public ReserveResult ReserveResult { get; set; } = ReserveResult.Success;
    public Reservation? Reservation { get; set; }
    public ConfirmResult ConfirmResultValue { get; set; } = ConfirmResult.Success;
    public ReleaseResult ReleaseResultValue { get; set; } = ReleaseResult.Success;
    public int SweepCount { get; private set; }

    public Task<Product> CreateProductAsync(string name, int stock) =>
        Task.FromResult(new Product { Id = Guid.NewGuid(), Name = name, Stock = stock });

    public Task<Product?> GetProductAsync(Guid id) => Task.FromResult<Product?>(null);

    public Task<(ReserveResult Result, Reservation? Reservation)> TryReserveAsync(
        Guid productId, int quantity, int ttlSeconds) => Task.FromResult((ReserveResult, Reservation));

    public Task<Reservation?> GetReservationAsync(Guid id) => Task.FromResult(Reservation);

    public Task<ConfirmResult> ConfirmAsync(Guid reservationId) => Task.FromResult(ConfirmResultValue);

    public Task<ReleaseResult> ReleaseAsync(Guid reservationId) => Task.FromResult(ReleaseResultValue);

    public Task SweepExpiredAsync()
    {
        SweepCount++;
        return Task.CompletedTask;
    }
}
