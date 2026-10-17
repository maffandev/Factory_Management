using System;

namespace Application.DTOs.Workflow;

public class ArticleDepartmentStatusDto
{
    public int Id { get; set; }
    public int ArticleId { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    public int? InputQuantity { get; set; }
    public int? OutputQuantity { get; set; }
    public int? LossQuantity { get; set; }

    public string? Note { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? DurationDisplay { get; set; }

    public string? UpdatedByUsername { get; set; }
    public string? ArticleCode { get; set; }
    public string? CompanyName { get; set; }
    public string Category { get; set; } = "Knit";
    public DateTime? DeliveryDate { get; set; }
    public bool IsPinned { get; set; }
    public bool CanStart { get; set; }
    public int SamplingAttemptCount { get; set; }
    public string? SamplingApprovalState { get; set; }
    public DateTime? SamplingSubmittedAt { get; set; }
    public DateTime? SamplingReviewedAt { get; set; }
    public string? SamplingReviewNote { get; set; }

    public int TotalDeliverableQuantity { get; set; }
    public int TotalDeliveredQuantity { get; set; }
    public int RemainingDeliverableQuantity { get; set; }

    public int ElapsedDays { get; set; }
    public bool IsDelayed { get; set; }
}
