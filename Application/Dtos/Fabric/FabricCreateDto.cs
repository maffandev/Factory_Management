using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Fabric;

public class FabricCreateDto
{
    [StringLength(50)]
    public string FabricCode { get; set; } = string.Empty;

    public string? InvNum { get; set; }

    public DateTime? FabricDate { get; set; }

    [StringLength(50)]
    public string FabricType { get; set; } = string.Empty;

    [StringLength(50)]
    public string Color { get; set; } = string.Empty;

    [StringLength(150)]
    public string? FabricBy { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Quantity cannot be negative.")]
    public decimal? Quantity { get; set; }

    public string? Unit { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative.")]
    public decimal? Rate { get; set; }
}
