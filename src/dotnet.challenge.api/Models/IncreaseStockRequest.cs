using System.ComponentModel.DataAnnotations;

namespace dotnet.challenge.api.Models;

public class IncreaseStockRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}
