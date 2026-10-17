using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs.Fabric;

public class FabricTopUpDto
{
    public int FabricId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Additional quantity must be greater than zero.")]
    public decimal AddedQuantity { get; set; }
    public decimal? NewRate { get; set; }
    public DateTime TopUpDate { get; set; }
}
