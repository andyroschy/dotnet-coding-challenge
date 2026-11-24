using System.ComponentModel.DataAnnotations;
using dotnet.challenge.api.Models;

namespace dotnet.challenge.tests;

public class ValidationTests
{
    private static bool IsValid(CreateReservationRequest request)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(
            request, new ValidationContext(request), results, validateAllProperties: true);
    }

    [Fact]
    public void CreateReservation_WithAllFields_IsValid()
    {
        var request = new CreateReservationRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            TtlSeconds = 60
        };

        IsValid(request).Should().BeTrue();
    }

    [Fact]
    public void CreateReservation_WithMissingProductId_IsInvalid()
    {
        var request = new CreateReservationRequest { Quantity = 1, TtlSeconds = 60 };

        IsValid(request).Should().BeFalse();
    }

    [Fact]
    public void CreateReservation_WithExplicitEmptyProductId_IsInvalid()
    {
        var request = new CreateReservationRequest
        {
            ProductId = Guid.Empty,
            Quantity = 1,
            TtlSeconds = 60
        };

        IsValid(request).Should().BeFalse();
    }
}
