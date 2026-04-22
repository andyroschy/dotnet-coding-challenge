using dotnet.challenge.api.Services;

namespace dotnet.challenge.api.Background;

/// <summary>
/// Periodically sweeps Active reservations whose ExpiresAt has passed and returns
/// their quantity to the owning product's Stock.
/// </summary>
public class ReservationExpirySweeper : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReservationExpirySweeper> _logger;

    public ReservationExpirySweeper(IServiceProvider services, ILogger<ReservationExpirySweeper> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => Loop(cancellationToken));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Loop(CancellationToken ct)
    {
        while (true)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));

            try
            {
                using var scope = _services.CreateScope();
                var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                inventory.SweepExpiredAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiry sweep failed");
            }
        }
    }
}
