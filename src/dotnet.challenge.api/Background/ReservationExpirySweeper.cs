using dotnet.challenge.api.Services;

namespace dotnet.challenge.api.Background;

/// <summary>
/// Periodically sweeps Active reservations whose ExpiresAt has passed and returns
/// their quantity to the owning product's Stock.
/// </summary>
public class ReservationExpirySweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReservationExpirySweeper> _logger;

    public ReservationExpirySweeper(IServiceProvider services, ILogger<ReservationExpirySweeper> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                    await inventory.SweepExpiredAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expiry sweep failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: the host requested cancellation.
        }
    }
}
