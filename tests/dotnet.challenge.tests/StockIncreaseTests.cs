using System.ComponentModel.DataAnnotations;
using dotnet.challenge.api.Controllers;
using dotnet.challenge.api.Models;
using dotnet.challenge.api.Services;
using dotnet.challenge.data.Cache;
using dotnet.challenge.data.Entities;
using dotnet.challenge.tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace dotnet.challenge.tests;

public class StockIncreaseTests
{
    private static InventoryService NewService() => new(
        new SimpleObjectCache<Guid, Product>(),
        new SimpleObjectCache<Guid, Reservation>());

    private static bool IsValid(IncreaseStockRequest request)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(
            request, new ValidationContext(request), results, validateAllProperties: true);
    }

    [Fact]
    public async Task IncreaseStock_AddsQuantityAndPersistsIt()
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", 5);

        var (result, updated) = await svc.IncreaseStockAsync(product.Id, 4);

        result.Should().Be(IncreaseStockResult.Success);
        updated!.Stock.Should().Be(9);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(9);
    }

    [Fact]
    public async Task IncreaseStock_ServiceWithUnknownProduct_ReturnsNotFound()
    {
        var svc = NewService();

        var (result, product) = await svc.IncreaseStockAsync(Guid.NewGuid(), 1);

        result.Should().Be(IncreaseStockResult.NotFound);
        product.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task IncreaseStock_NonPositiveQuantity_IsRejected(int quantity)
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", 5);

        var (result, _) = await svc.IncreaseStockAsync(product.Id, quantity);

        result.Should().Be(IncreaseStockResult.InvalidQuantity);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(5);
    }

    [Fact]
    public async Task IncreaseStock_Overflow_IsRejectedWithoutChangingStock()
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", int.MaxValue - 1);

        var (result, updated) = await svc.IncreaseStockAsync(product.Id, 2);

        result.Should().Be(IncreaseStockResult.Overflow);
        updated.Should().BeNull();
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(int.MaxValue - 1);
    }

    [Fact]
    public async Task IncreaseStock_ConcurrentCalls_AreSerialized()
    {
        var svc = NewService();
        var product = await svc.CreateProductAsync("Widget", 0);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => svc.IncreaseStockAsync(product.Id, 1)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.Result == IncreaseStockResult.Success);
        (await svc.GetProductAsync(product.Id))!.Stock.Should().Be(20);
    }

    [Fact]
    public void IncreaseStockRequest_PositiveQuantity_IsValid()
    {
        IsValid(new IncreaseStockRequest { Quantity = 1 }).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IncreaseStockRequest_NonPositiveQuantity_IsInvalid(int quantity)
    {
        IsValid(new IncreaseStockRequest { Quantity = quantity }).Should().BeFalse();
    }

    [Fact]
    public async Task IncreaseStock_Success_ReturnsOkWithUpdatedProduct()
    {
        var product = new Product { Id = Guid.NewGuid(), Stock = 7 };
        var fake = new FakeInventoryService { IncreasedProduct = product };
        var controller = new ProductsController(fake);

        var result = await controller.IncreaseStock(product.Id, new IncreaseStockRequest { Quantity = 2 });

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(product);
    }

    [Fact]
    public async Task IncreaseStock_ControllerWithUnknownProduct_ReturnsNotFound()
    {
        var fake = new FakeInventoryService { IncreaseStockResultValue = IncreaseStockResult.NotFound };
        var controller = new ProductsController(fake);

        var result = await controller.IncreaseStock(Guid.NewGuid(), new IncreaseStockRequest { Quantity = 1 });

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IncreaseStock_InvalidQuantity_ReturnsBadRequest()
    {
        var fake = new FakeInventoryService { IncreaseStockResultValue = IncreaseStockResult.InvalidQuantity };
        var controller = new ProductsController(fake);

        var result = await controller.IncreaseStock(Guid.NewGuid(), new IncreaseStockRequest { Quantity = 0 });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IncreaseStock_Overflow_ReturnsConflict()
    {
        var fake = new FakeInventoryService { IncreaseStockResultValue = IncreaseStockResult.Overflow };
        var controller = new ProductsController(fake);

        var result = await controller.IncreaseStock(Guid.NewGuid(), new IncreaseStockRequest { Quantity = 1 });

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
