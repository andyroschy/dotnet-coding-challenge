using dotnet.challenge.api.Controllers;
using dotnet.challenge.api.Models;
using dotnet.challenge.api.Services;
using dotnet.challenge.data.Entities;
using dotnet.challenge.tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace dotnet.challenge.tests;

public class ControllerTests
{
    private static CreateReservationRequest ValidRequest() => new()
    {
        ProductId = Guid.NewGuid(),
        Quantity = 1,
        TtlSeconds = 60
    };

    [Fact]
    public async Task CreateReservation_InsufficientStock_ReturnsConflict()
    {
        var fake = new FakeInventoryService { ReserveResult = ReserveResult.InsufficientStock };
        var controller = new ReservationsController(fake);

        var result = await controller.CreateReservation(ValidRequest());

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateReservation_UnknownProduct_ReturnsNotFound()
    {
        var fake = new FakeInventoryService { ReserveResult = ReserveResult.ProductNotFound };
        var controller = new ReservationsController(fake);

        var result = await controller.CreateReservation(ValidRequest());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateReservation_Success_ReturnsCreatedWithLocationTarget()
    {
        var reservation = new Reservation { Id = Guid.NewGuid() };
        var fake = new FakeInventoryService
        {
            ReserveResult = ReserveResult.Success,
            Reservation = reservation
        };
        var controller = new ReservationsController(fake);

        var result = await controller.CreateReservation(ValidRequest());

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(ReservationsController.GetReservation));
        created.RouteValues!["id"].Should().Be(reservation.Id);
        created.Value.Should().BeSameAs(reservation);
    }

    [Fact]
    public async Task Confirm_IllegalState_ReturnsConflict()
    {
        var fake = new FakeInventoryService { ConfirmResultValue = ConfirmResult.IllegalState };
        var controller = new ReservationsController(fake);

        var result = await controller.Confirm(Guid.NewGuid());

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Release_SuccessAndAlreadyReleased_ReturnNoContent()
    {
        var fake = new FakeInventoryService { ReleaseResultValue = ReleaseResult.Success };
        var controller = new ReservationsController(fake);

        (await controller.Release(Guid.NewGuid())).Should().BeOfType<NoContentResult>();

        fake.ReleaseResultValue = ReleaseResult.AlreadyReleased;
        (await controller.Release(Guid.NewGuid())).Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Release_ConfirmedReservation_ReturnsConflict()
    {
        var fake = new FakeInventoryService { ReleaseResultValue = ReleaseResult.IllegalState };
        var controller = new ReservationsController(fake);

        var result = await controller.Release(Guid.NewGuid());

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
