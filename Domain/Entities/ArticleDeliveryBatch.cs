using Domain.Common;

namespace Domain.Entities;

public class ArticleDeliveryBatch
{
    public int Id { get; set; }

    public int ArticleId { get; set; }
    public Article Article { get; set; } = null!;

    public int BatchNumber { get; set; }
    public int BGradeDelivered { get; set; }
    public int TotalDeliveredInBatch { get; set; }

    public string? PackedBy { get; set; }
    public string? CheckedBy { get; set; }
    public int? NoOfCartons { get; set; }
    public string? Note { get; set; }

    public int DeliveredByUserId { get; set; }
    public User DeliveredBy { get; set; } = null!;
    public DateTime DeliveredAt { get; set; } = PakistanTime.Now;

    public ICollection<ArticleDeliveryBatchItem> Items { get; set; } = new List<ArticleDeliveryBatchItem>();
}

public class ArticleDeliveryBatchItem
{
    public int Id { get; set; }

    public int ArticleDeliveryBatchId { get; set; }
    public ArticleDeliveryBatch Batch { get; set; } = null!;

    public string SizeLabel { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int QuantityDelivered { get; set; }
}
