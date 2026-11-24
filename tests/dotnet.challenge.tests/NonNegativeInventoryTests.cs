using System.ComponentModel.DataAnnotations;
using dotnet.challenge.api.Controllers;
using dotnet.challenge.api.Models;
using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;
using dotnet.challenge.tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace dotnet.challenge.tests;

public class NonNegativeInventoryTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    private static bool IsValid<T>(T model)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(
            model!, new ValidationContext(model!), results, validateAllProperties: true);
    }

    [Fact]
    public async Task CreateProduct_NegativeStock_Throws()
    {
        var svc = NewService();
        Func<Task> act = () => svc.CreateProductAsync("Widget", -1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CreateProduct_ZeroStock_IsAllowed()
    {
        var svc = NewService();

        var product = await svc.CreateProductAsync("Widget", 0);

        product.Stock.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Reserve_NonPositiveQuantity_IsRejectedWithoutChangingStock(int quantity)
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", 5);

        var (result, reservation) = await svc.TryReserveAsync(product.Id, quantity, ttlSeconds: 60);

        result.Should().Be(ReserveResult.InvalidQuantity);
        reservation.Should().BeNull();
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public void CreateProduct_RequestWithNegativeStock_IsInvalid()
    {
        var request = new CreateProductRequest { Name = "Widget", Stock = -1 };

        IsValid(request).Should().BeFalse();
    }

    [Fact]
    public void CreateProduct_RequestWithZeroStock_IsValid()
    {
        var request = new CreateProductRequest { Name = "Widget", Stock = 0 };

        IsValid(request).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void CreateReservation_RequestWithNonPositiveQuantity_IsInvalid(int quantity)
    {
        var request = new CreateReservationRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = quantity,
            TtlSeconds = 60
        };

        IsValid(request).Should().BeFalse();
    }

    [Fact]
    public async Task CreateReservation_InvalidQuantity_ReturnsBadRequest()
    {
        var fake = new FakeInventoryService { ReserveResult = ReserveResult.InvalidQuantity };
        var controller = new ReservationsController(fake);

        var result = await controller.CreateReservation(new CreateReservationRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 0,
            TtlSeconds = 60
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
