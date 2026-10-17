using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Fabric;

public class FabricOutDto
{
    public int FabricId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Out quantity must be greater than zero.")]
    public decimal OutQuantity { get; set; }
}
