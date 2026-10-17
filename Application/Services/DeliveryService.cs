using Application.DTOs.Workflow;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class DeliveryService : IDeliveryService
{
    private readonly IArticleRepository _articleRepository;
    private readonly IStatusLogRepository _statusLogRepository;

    public DeliveryService(
        IArticleRepository articleRepository,
        IStatusLogRepository statusLogRepository)
    {
        _articleRepository = articleRepository;
        _statusLogRepository = statusLogRepository;
    }

    public async Task<bool> ConfirmDeliveryAsync(DeliveryConfirmDto dto, int updatedByUserId)
    {
        var article = await _articleRepository.GetByIdAsync(dto.ArticleId);
        if (article == null)
            throw new InvalidOperationException("Article not found.");

        var deliveryStatus = article.DepartmentStatuses
            .FirstOrDefault(s => s.Department.Type == DepartmentType.Delivery);

        if (deliveryStatus == null)
            throw new InvalidOperationException("Delivery step not found for this article.");

        if (deliveryStatus.Status != DepartmentStatus.InProcess)
            throw new InvalidOperationException("Delivery work has not been started.");

        if (dto.BGradeToDeliver < 0 || dto.SizeDeliveries.Any(x => x.QuantityToDeliver < 0))
            throw new InvalidOperationException("Delivery quantities cannot be negative.");

        if (dto.NoOfCartons.HasValue && dto.NoOfCartons.Value < 0)
            throw new InvalidOperationException("Number of cartons cannot be negative.");

        if (dto.SizeDeliveries
            .Where(x => x.QuantityToDeliver > 0)
            .GroupBy(x => (x.SizeLabel ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate sizes are not allowed in a delivery batch.");

        var totalBGrade = article.BGradeQuantity ?? 0;
        var remainingBGrade = Math.Max(0, totalBGrade - article.DeliveredBGradeQuantity);

        if (dto.BGradeToDeliver > remainingBGrade)
            throw new InvalidOperationException(
                $"B-Grade delivery quantity ({dto.BGradeToDeliver}) cannot exceed remaining B-Grade quantity ({remainingBGrade}).");

        var hasSizeBreakdowns = article.SizeBreakdowns.Count > 0;
        var sizeSumInBatch = 0;

        if (hasSizeBreakdowns)
        {
            foreach (var entry in dto.SizeDeliveries)
            {
                if (entry.QuantityToDeliver <= 0)
                    continue;

                var sb = article.SizeBreakdowns.FirstOrDefault(s =>
                    string.Equals(s.SizeLabel.Trim(), entry.SizeLabel.Trim(), StringComparison.OrdinalIgnoreCase));

                if (sb == null)
                    throw new InvalidOperationException($"Size '{entry.SizeLabel}' does not exist in Quality breakdown.");

                var remainingForSize = Math.Max(0, sb.Quantity - sb.DeliveredQuantity);
                if (entry.QuantityToDeliver > remainingForSize)
                {
                    throw new InvalidOperationException(
                        $"Delivery quantity ({entry.QuantityToDeliver}) for size '{sb.SizeLabel}' cannot exceed its remaining quantity ({remainingForSize}).");
                }

                sizeSumInBatch += entry.QuantityToDeliver;
            }
        }

        var totalInBatch = sizeSumInBatch + dto.BGradeToDeliver;
        var totalDeliverable = article.SizeBreakdowns.Sum(s => s.Quantity) + totalBGrade;

        if (totalDeliverable > 0 && totalInBatch <= 0)
        {
            throw new InvalidOperationException("Please enter at least 1 piece (size-wise or B-Grade) to deliver in this batch.");
        }

        var nextBatchNumber = article.DeliveryBatches.Any()
            ? article.DeliveryBatches.Max(b => b.BatchNumber) + 1
            : 1;

        var batch = new ArticleDeliveryBatch
        {
            ArticleId = article.Id,
            BatchNumber = nextBatchNumber,
            BGradeDelivered = dto.BGradeToDeliver,
            TotalDeliveredInBatch = totalInBatch,
            PackedBy = string.IsNullOrWhiteSpace(dto.PackedBy) ? null : dto.PackedBy.Trim(),
            CheckedBy = string.IsNullOrWhiteSpace(dto.CheckedBy) ? null : dto.CheckedBy.Trim(),
            NoOfCartons = dto.NoOfCartons,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            DeliveredByUserId = updatedByUserId,
            DeliveredAt = PakistanTime.Now
        };

        foreach (var sb in article.SizeBreakdowns.OrderBy(s => s.OrderIndex))
        {
            var entry = dto.SizeDeliveries.FirstOrDefault(x =>
                string.Equals(x.SizeLabel.Trim(), sb.SizeLabel.Trim(), StringComparison.OrdinalIgnoreCase));

            var qtyToDeliver = entry?.QuantityToDeliver ?? 0;
            if (qtyToDeliver > 0)
            {
                sb.DeliveredQuantity += qtyToDeliver;
                batch.Items.Add(new ArticleDeliveryBatchItem
                {
                    SizeLabel = sb.SizeLabel,
                    OrderIndex = sb.OrderIndex,
                    QuantityDelivered = qtyToDeliver
                });
            }
        }

        article.DeliveredBGradeQuantity += dto.BGradeToDeliver;
        article.DeliveryBatches.Add(batch);

        if (!string.IsNullOrWhiteSpace(dto.PackedBy))
            article.PackedBy = dto.PackedBy.Trim();
        if (!string.IsNullOrWhiteSpace(dto.CheckedBy))
            article.CheckedBy = dto.CheckedBy.Trim();
        if (dto.NoOfCartons.HasValue && dto.NoOfCartons.Value > 0)
            article.NoOfCartons = (article.NoOfCartons ?? 0) + dto.NoOfCartons.Value;

        var cumulativeDelivered = article.SizeBreakdowns.Sum(s => s.DeliveredQuantity) + article.DeliveredBGradeQuantity;
        var isFullyDelivered = totalDeliverable == 0 || cumulativeDelivered >= totalDeliverable;

        deliveryStatus.OutputQuantity = cumulativeDelivered;
        deliveryStatus.LossQuantity = Math.Max(0, (deliveryStatus.InputQuantity ?? totalDeliverable) - cumulativeDelivered);
        deliveryStatus.Note = dto.Note;
        deliveryStatus.UpdatedByUserId = updatedByUserId;

        if (isFullyDelivered)
        {
            deliveryStatus.Status = DepartmentStatus.Done;
            deliveryStatus.EndedAt = PakistanTime.Now;
            article.IsDelivered = true;
            article.DeliveredAt = PakistanTime.Now;
        }

        article.UpdatedAt = PakistanTime.Now;
        _articleRepository.Update(article);
        await _articleRepository.SaveChangesAsync();

        await _statusLogRepository.AddAsync(new StatusLog
        {
            ArticleDepartmentStatusId = deliveryStatus.Id,
            OldStatus = DepartmentStatus.InProcess,
            NewStatus = deliveryStatus.Status,
            OutputQuantity = deliveryStatus.OutputQuantity,
            LossQuantity = deliveryStatus.LossQuantity,
            ChangedByUserId = updatedByUserId,
            Note = isFullyDelivered
                ? $"Delivery Batch #{nextBatchNumber} ({totalInBatch} pcs) — All {cumulativeDelivered}/{totalDeliverable} pieces delivered."
                : $"Partial Delivery Batch #{nextBatchNumber} ({totalInBatch} pcs) — {cumulativeDelivered}/{totalDeliverable} pieces delivered."
        });
        await _statusLogRepository.SaveChangesAsync();

        return isFullyDelivered;
    }
}
