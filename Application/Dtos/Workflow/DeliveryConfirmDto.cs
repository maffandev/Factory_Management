using System.Collections.Generic;

namespace Application.DTOs.Workflow;

public class DeliveryConfirmDto
{
    public int ArticleId { get; set; }
    public string? PackedBy { get; set; }
    public string? CheckedBy { get; set; }
    public int? NoOfCartons { get; set; }
    public string? Note { get; set; }
    public int BGradeToDeliver { get; set; }
    public List<SizeDeliveryEntryDto> SizeDeliveries { get; set; } = new();
}

public class SizeDeliveryEntryDto
{
    public string SizeLabel { get; set; } = string.Empty;
    public int QuantityToDeliver { get; set; }
}
