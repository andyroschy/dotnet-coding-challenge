using dotnet.challenge.api.Background;
using dotnet.challenge.api.Services;
using dotnet.challenge.tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;

namespace dotnet.challenge.tests;

public class SweeperTests
{
    [Fact]
    public async Task Sweeper_IsBackgroundService_AndStopsCleanlyOnCancellation()
    {
        var fake = new FakeInventoryService();
        using var provider = new ServiceCollection()
            .AddSingleton<IInventoryService>(fake)
            .BuildServiceProvider();

        var sweeper = new ReservationExpirySweeper(
            provider,
            NullLogger<ReservationExpirySweeper>.Instance);

        sweeper.Should().BeAssignableTo<BackgroundService>();

        using var cts = new CancellationTokenSource();
        await sweeper.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        await sweeper.StopAsync(cts.Token);

        fake.SweepCount.Should().BeGreaterThan(0);
    }
}
