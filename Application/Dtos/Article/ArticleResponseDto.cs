using System;
using System.Collections.Generic;

namespace Application.DTOs.Article;

public class ArticleResponseDto
{
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string ArticleCode { get; set; } = string.Empty;

    public string? Color { get; set; }

    public string Category { get; set; } = "Knit";

    public DateTime OrderDate { get; set; }

    public DateTime DeliveryDate { get; set; }

    public string? Season { get; set; }

    public bool EmbellishmentEmbroidery { get; set; }

    public bool EmbellishmentPrinting { get; set; }

    public bool EmbellishmentHandwork { get; set; }

    public int? Quantity { get; set; }

    public decimal? PricePerPiece { get; set; }

    public decimal? PriceTotal { get; set; }

    public int DoneDepartments { get; set; }

    public int TotalDepartments { get; set; }

    public string? CurrentDepartmentName { get; set; }

    public string? CurrentDepartmentStatus { get; set; }

    public int CurrentDepartmentDays { get; set; }

    public bool IsDepartmentDelayed { get; set; }

    public bool IsPinned { get; set; }

    public bool IsDelivered { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public bool CuttingStarted { get; set; }

    public string? AssignedTeamName { get; set; }

    public string? StitchedBy { get; set; }

    public int? BGradeQuantity { get; set; }

    public int AGradeQuantity { get; set; }

    public int DeliveredAGradeQuantity { get; set; }

    public int DeliveredBGradeQuantity { get; set; }

    public int TotalDeliverableQuantity { get; set; }

    public int TotalDeliveredQuantity { get; set; }

    public int RemainingDeliverableQuantity { get; set; }

    // Total loss recorded across all of this article's departments.
    public int TotalLossQuantity { get; set; }

    // Article-level loss: ordered quantity minus A-Grade and B-Grade output.
    public int? LossQuantity { get; set; }

    public List<SizeBreakdownEntryDto> SizeBreakdowns { get; set; } = new();

    public List<SizeBreakdownEntryDto> CuttingSizeBreakdowns { get; set; } = new();

    public List<DeliveryBatchDto> DeliveryBatches { get; set; } = new();

    public int CuttingSizeTotal { get; set; }
    public int PreQualityLossQuantity { get; set; }
    public int CGradeQuantity { get; set; }

    public string? PackedBy { get; set; }

    public string? CheckedBy { get; set; }

    public int? NoOfCartons { get; set; }

    public string CreatedByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<string> AlternateCodes { get; set; } = new();

    public List<ArticleFabricDto> Fabrics { get; set; } = new();
}

public class SizeBreakdownEntryDto
{
    public string SizeLabel { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public int Quantity { get; set; }

    public int DeliveredQuantity { get; set; }

    public int RemainingQuantity => Math.Max(0, Quantity - DeliveredQuantity);
}

public class DeliveryBatchDto
{
    public int Id { get; set; }
    public int BatchNumber { get; set; }
    public int BGradeDelivered { get; set; }
    public int TotalDeliveredInBatch { get; set; }
    public string? PackedBy { get; set; }
    public string? CheckedBy { get; set; }
    public int? NoOfCartons { get; set; }
    public string? Note { get; set; }
    public string DeliveredByUsername { get; set; } = string.Empty;
    public DateTime DeliveredAt { get; set; }
    public List<DeliveryBatchItemDto> Items { get; set; } = new();
}

public class DeliveryBatchItemDto
{
    public string SizeLabel { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int QuantityDelivered { get; set; }
}
