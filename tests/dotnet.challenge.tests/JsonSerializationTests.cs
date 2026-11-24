using System.Text.Json;
using System.Text.Json.Serialization;
using dotnet.challenge.api;
using dotnet.challenge.data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace dotnet.challenge.tests;

public class JsonSerializationTests
{
    [Fact]
    public void ReservationStatus_IsSerializedAsText()
    {
        var services = new ServiceCollection();
        new Startup(new ConfigurationBuilder().Build()).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var jsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        jsonOptions.Converters.Should().Contain(converter => converter is JsonStringEnumConverter);

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            Status = ReservationStatus.Active
        };

        var json = JsonSerializer.Serialize(reservation, jsonOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("status").GetString().Should().Be("Active");
    }
}
