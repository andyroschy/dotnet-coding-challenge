using System.ComponentModel.DataAnnotations;

namespace dotnet.challenge.api.Models;

public class CreateReservationRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(1, 3600)]
    public int TtlSeconds { get; set; }
}
