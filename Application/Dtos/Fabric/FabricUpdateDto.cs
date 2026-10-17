using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Fabric;


public class FabricUpdateDto
{
    public int Id { get; set; }

    [StringLength(150)]
    public string? FabricBy { get; set; }

    [StringLength(50)]
    public string? InvNum { get; set; }

    public DateTime? FabricDate { get; set; }

    [Required(ErrorMessage = "Fabric Type is required.")]
    [StringLength(50)]
    public string FabricType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Unit is required.")]
    public string Unit { get; set; } = "Meter";

    [Range(0, double.MaxValue, ErrorMessage = "Rate cannot be negative.")]
    public decimal? Rate { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Quantity cannot be negative.")]
    public decimal? Quantity { get; set; }
}
