using Application.DTOs.Workflow;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class WorkflowService : IWorkflowService
{
    private readonly IArticleDepartmentStatusRepository _statusRepository;
    private readonly IArticleRepository _articleRepository;
    private readonly IFabricRepository _fabricRepository;
    private readonly IStatusLogRepository _statusLogRepository;

    public WorkflowService(
        IArticleDepartmentStatusRepository statusRepository,
        IArticleRepository articleRepository,
        IFabricRepository fabricRepository,
        IStatusLogRepository statusLogRepository)
    {
        _statusRepository = statusRepository;
        _articleRepository = articleRepository;
        _fabricRepository = fabricRepository;
        _statusLogRepository = statusLogRepository;
    }

    /// <summary>
    /// Self-heals any article whose Printing/Embroidery/HandWork/Stitching was accidentally given
    /// a SequenceNumber after Quality &amp; Packing or Delivery while Quality/Delivery are still Pending.
    /// </summary>
    private async Task<List<ArticleDepartmentStatus>> EnsureValidSequenceOrderAsync(List<ArticleDepartmentStatus> statuses)
    {
        if (statuses.Count <= 1)
            return statuses;

        var finishTypes = new[] { DepartmentType.QualityAndPacking, DepartmentType.Delivery };
        var earliestFinishSeq = statuses
            .Where(s => s.Department != null && finishTypes.Contains(s.Department.Type) && s.Status == DepartmentStatus.Pending)
            .Select(s => (int?)s.SequenceNumber)
            .Min();

        if (!earliestFinishSeq.HasValue)
            return statuses;

        var misplacedProd = statuses.Any(s =>
            s.Department != null &&
            !finishTypes.Contains(s.Department.Type) &&
            s.SequenceNumber > earliestFinishSeq.Value);

        if (!misplacedProd)
            return statuses;

        // Reorder so all production departments (Pattern, Sampling, Cutting, Printing, Embroidery, Stitching, HandWork)
        // come before Quality & Packing and Delivery, preserving their relative order.
        var prod = statuses
            .Where(s => s.Department == null || !finishTypes.Contains(s.Department.Type))
            .OrderBy(s => s.SequenceNumber)
            .ThenBy(s => s.Department?.OrderIndex ?? 5)
            .ToList();

        var finish = statuses
            .Where(s => s.Department != null && finishTypes.Contains(s.Department.Type))
            .OrderBy(s => s.Department.OrderIndex)
            .ToList();

        var reordered = prod.Concat(finish).ToList();
        var seq = 1;
        foreach (var item in reordered)
        {
            item.SequenceNumber = seq++;
            _statusRepository.Update(item);
        }

        await _statusRepository.SaveChangesAsync();
        return reordered;
    }

    public async Task<List<ArticleDepartmentStatusDto>> GetByArticleAsync(int articleId)
    {
        var statuses = await _statusRepository.GetByArticleAsync(articleId);
        statuses = await EnsureValidSequenceOrderAsync(statuses);
        var ordered = statuses.OrderBy(s => s.SequenceNumber).ToList();
        var activeStep = ordered.FirstOrDefault(s => s.Status != DepartmentStatus.Done && !(s.Article?.IsDelivered ?? false));

        return ordered.Select(s =>
        {
            var dto = MapToDto(s);
            if (s.Status == DepartmentStatus.Done && s.StartedAt.HasValue && s.EndedAt.HasValue)
            {
                var d = (s.EndedAt.Value - s.StartedAt.Value).TotalDays;
                dto.ElapsedDays = Math.Max(0, (int)Math.Floor(d));
                dto.IsDelayed = d >= PakistanTime.DepartmentDelayThresholdDays;
            }
            else if (activeStep != null && s.Id == activeStep.Id)
            {
                var prevDone = ordered
                    .Where(x => x.SequenceNumber < s.SequenceNumber && x.Status == DepartmentStatus.Done)
                    .OrderByDescending(x => x.SequenceNumber)
                    .FirstOrDefault();
                var holdSince = prevDone?.EndedAt ?? s.StartedAt ?? s.Article?.CreatedAt ?? PakistanTime.Now;
                var d = (PakistanTime.Now - holdSince).TotalDays;
                dto.ElapsedDays = Math.Max(0, (int)Math.Floor(d));
                dto.IsDelayed = d >= PakistanTime.DepartmentDelayThresholdDays;
            }
            return dto;
        }).ToList();
    }

    public async Task<ArticleDepartmentStatusDto?> GetStatusByIdAsync(int statusId)
    {
        var status = await _statusRepository.GetByIdAsync(statusId);
        return status == null ? null : MapToDto(status);
    }

    public async Task StartWorkAsync(StartDepartmentWorkDto dto, int updatedByUserId)
    {
        var status = await _statusRepository.GetByIdAsync(dto.ArticleDepartmentStatusId);
        if (status == null)
            throw new InvalidOperationException("Workflow record not found.");

        if (status.Status != DepartmentStatus.Pending)
            throw new InvalidOperationException("This department's work has already been started or completed.");

        // Rule: the previous department (by SequenceNumber) must be Done before this one can start.
        var allForArticle = await _statusRepository.GetByArticleAsync(status.ArticleId);
        allForArticle = await EnsureValidSequenceOrderAsync(allForArticle);

        // Refresh current status sequence from normalized list
        var currentInList = allForArticle.FirstOrDefault(s => s.Id == status.Id);
        var currentSeq = currentInList?.SequenceNumber ?? status.SequenceNumber;
        status.SequenceNumber = currentSeq;

        var previous = allForArticle
            .Where(s => s.SequenceNumber < currentSeq)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefault();

        if (previous != null && previous.Status != DepartmentStatus.Done)
            throw new InvalidOperationException($"Cannot start - previous department '{previous.Department.Name}' is not finished yet.");

        // Rule: Cutting requires at least one fabric linked to the article.
        if (status.Department.Type == DepartmentType.Cutting)
        {
            var article = await _articleRepository.GetByIdAsync(status.ArticleId);
            if (article == null || !article.FabricLinks.Any())
                throw new InvalidOperationException("Cannot start Cutting - no fabric has been added to this article yet.");
        }

        // Rule: Stitching requires a team assignment.
        if (status.Department.Type == DepartmentType.Stitching)
        {
            if (dto.AssignedTeamId == null)
                throw new InvalidOperationException("Please assign a Stitching Team before starting Stitching.");

            var article = await _articleRepository.GetByIdAsync(status.ArticleId);
            article!.AssignedTeamId = dto.AssignedTeamId;
            _articleRepository.Update(article);
        }

        if (previous != null)
        {
            status.InputQuantity = previous.OutputQuantity;
        }
        else
        {
            var article = await _articleRepository.GetByIdAsync(status.ArticleId);
            status.InputQuantity = article?.Quantity;
        }

        status.Status = DepartmentStatus.InProcess;
        status.StartedAt = PakistanTime.Now;
        status.UpdatedByUserId = updatedByUserId;

        _statusRepository.Update(status);
        await _statusRepository.SaveChangesAsync();

        await LogStatusChangeAsync(status, DepartmentStatus.Pending, updatedByUserId, "Work started.");
    }

    public async Task EndWorkAsync(EndDepartmentWorkDto dto, int updatedByUserId)
    {
        var status = await _statusRepository.GetByIdAsync(dto.ArticleDepartmentStatusId);
        if (status == null)
            throw new InvalidOperationException("Workflow record not found.");

        if (status.Status != DepartmentStatus.InProcess)
            throw new InvalidOperationException("This department's work has not been started yet.");

        // Sampling submission waits for the Pattern manager's decision. It is not final Done yet.
        if (status.Department.Type == DepartmentType.Sampling)
        {
            if (status.SamplingApprovalState == "AwaitingApproval")
                throw new InvalidOperationException("This Sampling attempt is already waiting for Pattern approval.");

            status.SamplingAttemptCount++;
            status.SamplingApprovalState = "AwaitingApproval";
            status.SamplingSubmittedAt = PakistanTime.Now;
            status.EndedAt = status.SamplingSubmittedAt;
            status.OutputQuantity = status.InputQuantity;
            status.LossQuantity = 0;
            status.Note = dto.Note;
            status.UpdatedByUserId = updatedByUserId;
            _statusRepository.Update(status);
            await _statusRepository.SaveChangesAsync();
            await LogStatusChangeAsync(status, DepartmentStatus.InProcess, updatedByUserId,
                $"Sampling attempt {status.SamplingAttemptCount} submitted for Pattern approval.");
            return;
        }

        // Pattern doesn't track quantity - just carries the input quantity forward.
        bool isQuantityless = status.Department.Type == DepartmentType.Pattern;

        if (isQuantityless)
        {
            status.OutputQuantity = status.InputQuantity;
            status.LossQuantity = 0;
        }
        else if (status.Department.Type == DepartmentType.Cutting)
        {
            if (dto.CuttingSizeBreakdowns.Count == 0)
                throw new InvalidOperationException("At least one Cutting size quantity is required.");
            if (dto.CuttingSizeBreakdowns.Any(x => string.IsNullOrWhiteSpace(x.SizeLabel) || x.Quantity < 0))
                throw new InvalidOperationException("Cutting sizes and quantities are invalid.");
            if (dto.CuttingSizeBreakdowns.GroupBy(x => x.SizeLabel.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Duplicate Cutting sizes are not allowed.");

            var article = await _articleRepository.GetByIdAsync(status.ArticleId)
                ?? throw new InvalidOperationException("Article not found.");
            article.CuttingSizeBreakdowns.Clear();
            foreach (var entry in dto.CuttingSizeBreakdowns.OrderBy(x => x.OrderIndex))
                article.CuttingSizeBreakdowns.Add(new ArticleCuttingSizeBreakdown
                {
                    SizeLabel = entry.SizeLabel.Trim(),
                    OrderIndex = entry.OrderIndex,
                    Quantity = entry.Quantity
                });

            status.OutputQuantity = dto.CuttingSizeBreakdowns.Sum(x => x.Quantity);
            status.LossQuantity = status.InputQuantity.HasValue
                ? Math.Max(0, status.InputQuantity.Value - status.OutputQuantity.Value)
                : 0;
            _articleRepository.Update(article);
        }
        else
        {
            if (dto.OutputQuantity == null)
                throw new InvalidOperationException("Output quantity is required to end work for this department.");
            if (dto.OutputQuantity.Value > (status.InputQuantity ?? 0))
                throw new InvalidOperationException($"Output quantity cannot exceed the input quantity. Maximum allowed is {status.InputQuantity ?? 0}.");

            status.OutputQuantity = dto.OutputQuantity;
            status.LossQuantity = (status.InputQuantity ?? 0) - dto.OutputQuantity.Value;
        }

        if (status.Department.Type == DepartmentType.Stitching)
        {
            if (string.IsNullOrWhiteSpace(dto.StitchedBy))
                throw new InvalidOperationException("Stitched By (Name) is required to end Stitching.");

            var article = await _articleRepository.GetByIdAsync(status.ArticleId);
            if (article == null) throw new InvalidOperationException("Article not found.");
            article.StitchedBy = dto.StitchedBy.Trim();
            _articleRepository.Update(article);
        }

        status.Note = dto.Note;
        status.Status = DepartmentStatus.Done;
        status.EndedAt = PakistanTime.Now;
        status.UpdatedByUserId = updatedByUserId;

        _statusRepository.Update(status);
        await _statusRepository.SaveChangesAsync();

        await LogStatusChangeAsync(status, DepartmentStatus.InProcess, updatedByUserId, dto.Note);
    }

    private async Task LogStatusChangeAsync(ArticleDepartmentStatus status, DepartmentStatus oldStatus, int userId, string? note)
    {
        var log = new StatusLog
        {
            ArticleDepartmentStatusId = status.Id,
            OldStatus = oldStatus,
            NewStatus = status.Status,
            OutputQuantity = status.OutputQuantity,
            LossQuantity = status.LossQuantity,
            ChangedByUserId = userId,
            ChangedAt = PakistanTime.Now,
            Note = note
        };

        await _statusLogRepository.AddAsync(log);
        await _statusLogRepository.SaveChangesAsync();
    }

    private static ArticleDepartmentStatusDto MapToDto(ArticleDepartmentStatus s)
    {
        var aGrade = s.Article?.SizeBreakdowns?.Sum(x => x.Quantity) ?? 0;
        var bGrade = s.Article?.BGradeQuantity ?? 0;
        var deliveredA = s.Article?.SizeBreakdowns?.Sum(x => x.DeliveredQuantity) ?? 0;
        var deliveredB = s.Article?.DeliveredBGradeQuantity ?? 0;
        var totalDeliverable = aGrade + bGrade;
        var totalDelivered = deliveredA + deliveredB;

        return new ArticleDepartmentStatusDto
        {
            Id = s.Id,
            ArticleId = s.ArticleId,
            DepartmentId = s.DepartmentId,
            DepartmentName = s.Department.Name,
            SequenceNumber = s.SequenceNumber,
            Status = s.Status.ToString(),
            InputQuantity = s.InputQuantity,
            OutputQuantity = s.OutputQuantity,
            LossQuantity = s.LossQuantity,
            Note = s.Note,
            StartedAt = s.StartedAt,
            EndedAt = s.EndedAt,
            DurationDisplay = (s.StartedAt.HasValue && s.EndedAt.HasValue)
                ? $"{(int)(s.EndedAt.Value - s.StartedAt.Value).TotalHours}h {(s.EndedAt.Value - s.StartedAt.Value).Minutes}m"
                : null,
            UpdatedByUsername = null,
            Category = s.Article?.Category.ToString() ?? "Knit",
            SamplingAttemptCount = s.SamplingAttemptCount,
            SamplingApprovalState = s.SamplingApprovalState,
            SamplingSubmittedAt = s.SamplingSubmittedAt,
            SamplingReviewedAt = s.SamplingReviewedAt,
            SamplingReviewNote = s.SamplingReviewNote,
            TotalDeliverableQuantity = totalDeliverable,
            TotalDeliveredQuantity = totalDelivered,
            RemainingDeliverableQuantity = Math.Max(0, totalDeliverable - totalDelivered)
        };
    }

    public async Task<List<ArticleDepartmentStatusDto>> GetPendingByDepartmentAsync(int departmentId, string? userCategory = null)
    {
        var statuses = await _statusRepository.GetByDepartmentAsync(departmentId);

        if (!string.IsNullOrWhiteSpace(userCategory) &&
            !string.Equals(userCategory, "Both", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<ArticleCategory>(userCategory, true, out var filterCat))
        {
            statuses = statuses.Where(s => s.Article.Category == filterCat).ToList();
        }

        var relevant = statuses
            .Where(s => s.Status != DepartmentStatus.Done && !s.Article.IsDelivered &&
                        !(s.Department.Type == DepartmentType.Sampling && s.SamplingApprovalState == "AwaitingApproval"))
            .ToList();

        var result = new List<ArticleDepartmentStatusDto>();
        var statusesByArticle = new Dictionary<int, List<ArticleDepartmentStatus>>();

        foreach (var s in relevant)
        {
            // One query per article instead of one per row.
            if (!statusesByArticle.TryGetValue(s.ArticleId, out var allForArticle))
            {
                allForArticle = await _statusRepository.GetByArticleAsync(s.ArticleId);
                allForArticle = await EnsureValidSequenceOrderAsync(allForArticle);
                statusesByArticle[s.ArticleId] = allForArticle;
            }
            var currentInList = allForArticle.FirstOrDefault(x => x.Id == s.Id);
            if (currentInList != null)
                s.SequenceNumber = currentInList.SequenceNumber;

            var dto = MapToDto(s);
            dto.ArticleCode = s.Article.ArticleCode;
            dto.CompanyName = s.Article.CompanyName;
            dto.Category = s.Article.Category.ToString();
            dto.DeliveryDate = s.Article.DeliveryDate;
            dto.IsPinned = s.Article.IsPinned;

            if (s.Status == DepartmentStatus.Pending)
            {
                var previous = allForArticle.Where(x => x.SequenceNumber < s.SequenceNumber);
                dto.CanStart = previous.All(x => x.Status == DepartmentStatus.Done);
            }
            else
            {
                dto.CanStart = true;
            }

            if (dto.CanStart)
            {
                var prevDone = allForArticle
                    .Where(x => x.SequenceNumber < s.SequenceNumber && x.Status == DepartmentStatus.Done)
                    .OrderByDescending(x => x.SequenceNumber)
                    .FirstOrDefault();
                var holdSince = prevDone?.EndedAt ?? s.StartedAt ?? s.Article.CreatedAt;
                var elapsed = (PakistanTime.Now - holdSince).TotalDays;
                dto.ElapsedDays = Math.Max(0, (int)Math.Floor(elapsed));
                dto.IsDelayed = elapsed >= PakistanTime.DepartmentDelayThresholdDays;
            }

            result.Add(dto);
        }

        return result
            .OrderByDescending(r => r.IsPinned)
            .ThenBy(r => r.DeliveryDate)
            .ToList();
    }

    public async Task<List<ArticleDepartmentStatusDto>> GetSamplingAwaitingApprovalAsync(string? userCategory = null)
    {
        var statuses = await _statusRepository.GetSamplingAwaitingApprovalAsync();

        if (!string.IsNullOrWhiteSpace(userCategory) &&
            !string.Equals(userCategory, "Both", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<ArticleCategory>(userCategory, true, out var filterCat))
        {
            statuses = statuses.Where(s => s.Article.Category == filterCat).ToList();
        }

        return statuses.Select(s =>
        {
            var dto = MapToDto(s);
            dto.ArticleCode = s.Article.ArticleCode;
            dto.CompanyName = s.Article.CompanyName;
            dto.Category = s.Article.Category.ToString();
            dto.DeliveryDate = s.Article.DeliveryDate;
            dto.IsPinned = s.Article.IsPinned;
            return dto;
        }).OrderByDescending(x => x.IsPinned).ThenBy(x => x.SamplingSubmittedAt).ToList();
    }

    public async Task ReviewSamplingAsync(int statusId, bool approved, string? note, int reviewedByUserId)
    {
        var status = await _statusRepository.GetByIdAsync(statusId)
            ?? throw new InvalidOperationException("Sampling workflow record not found.");
        if (status.Department.Type != DepartmentType.Sampling || status.SamplingApprovalState != "AwaitingApproval")
            throw new InvalidOperationException("This Sampling attempt is not waiting for approval.");

        status.SamplingReviewedAt = PakistanTime.Now;
        status.SamplingReviewedByUserId = reviewedByUserId;
        status.SamplingReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        status.UpdatedByUserId = reviewedByUserId;

        if (approved)
        {
            status.SamplingApprovalState = "Approved";
            status.Status = DepartmentStatus.Done;
        }
        else
        {
            status.SamplingApprovalState = "Rejected";
            status.Status = DepartmentStatus.InProcess;
            status.StartedAt = PakistanTime.Now;
            status.EndedAt = null;
            status.OutputQuantity = null;
            status.LossQuantity = null;
        }

        _statusRepository.Update(status);
        await _statusRepository.SaveChangesAsync();
        await LogStatusChangeAsync(status, DepartmentStatus.InProcess, reviewedByUserId,
            approved ? "Sampling approved by Pattern." : "Sampling rejected by Pattern; resampling requested.");
    }

    public async Task UndoLastDepartmentAsync(int articleId, int updatedByUserId)
    {
        var statuses = await _statusRepository.GetByArticleAsync(articleId);
        if (statuses.Count == 0)
            throw new InvalidOperationException("No department workflow defined for this article.");

        var article = await _articleRepository.GetByIdAsync(articleId);
        if (article != null && article.IsDelivered)
        {
            // Delegate to UndoDeliverAsync so the latest delivery batch is properly reverted.
            await UndoDeliverAsync(articleId, updatedByUserId);
            return;
        }

        // Case 1: a department is currently InProcess
        var inProcess = statuses.FirstOrDefault(s => s.Status == DepartmentStatus.InProcess);
        if (inProcess != null)
        {
            // If Delivery is InProcess and has partial delivery batches, undo the most recent batch first.
            if (inProcess.Department?.Type == DepartmentType.Delivery &&
                article != null &&
                article.DeliveryBatches.Any())
            {
                RevertLatestDeliveryBatch(article, inProcess);
                inProcess.UpdatedByUserId = updatedByUserId;
                _articleRepository.Update(article);
                _statusRepository.Update(inProcess);
                await _statusRepository.SaveChangesAsync();
                await LogStatusChangeAsync(inProcess, DepartmentStatus.InProcess, updatedByUserId,
                    "Undo: Reverted latest partial delivery batch.");
                return;
            }

            inProcess.Status = DepartmentStatus.Pending;
            inProcess.StartedAt = null;
            inProcess.InputQuantity = null;
            inProcess.SamplingApprovalState = null;
            inProcess.SamplingSubmittedAt = null;
            inProcess.UpdatedByUserId = updatedByUserId;
            _statusRepository.Update(inProcess);

            await _statusRepository.SaveChangesAsync();
            await LogStatusChangeAsync(inProcess, DepartmentStatus.InProcess, updatedByUserId,
                $"Undo: {inProcess.Department?.Name ?? "department"} InProcess -> Pending");
            return;
        }

        // Case 2: nothing InProcess → reopen highest-SequenceNumber Done to InProcess.
        var lastDone = statuses
            .Where(s => s.Status == DepartmentStatus.Done)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefault();

        if (lastDone != null)
        {
            lastDone.Status = DepartmentStatus.InProcess;
            lastDone.EndedAt = null;
            lastDone.OutputQuantity = null;
            lastDone.LossQuantity = null;
            lastDone.UpdatedByUserId = updatedByUserId;
            _statusRepository.Update(lastDone);

            await _statusRepository.SaveChangesAsync();
            await LogStatusChangeAsync(lastDone, DepartmentStatus.Done, updatedByUserId,
                $"Undo: {lastDone.Department?.Name ?? "department"} Done -> InProcess");
            return;
        }

        throw new InvalidOperationException("Nothing to undo for this article.");
    }

    public async Task UndoDeliverAsync(int articleId, int updatedByUserId)
    {
        var article = await _articleRepository.GetByIdAsync(articleId);
        if (article == null)
            throw new InvalidOperationException("Article not found.");

        if (!article.IsDelivered)
            throw new InvalidOperationException("This article is not delivered.");

        article.IsDelivered = false;
        article.DeliveredAt = null;

        var statuses = await _statusRepository.GetByArticleAsync(articleId);
        var lastDone = statuses
            .Where(s => s.Status == DepartmentStatus.Done)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefault();

        if (lastDone != null)
        {
            if (article.DeliveryBatches.Any())
            {
                RevertLatestDeliveryBatch(article, lastDone);
            }
            else
            {
                foreach (var sb in article.SizeBreakdowns)
                    sb.DeliveredQuantity = 0;
                article.DeliveredBGradeQuantity = 0;
                lastDone.OutputQuantity = null;
                lastDone.LossQuantity = null;
            }

            lastDone.Status = DepartmentStatus.InProcess;
            lastDone.EndedAt = null;
            lastDone.UpdatedByUserId = updatedByUserId;
            _statusRepository.Update(lastDone);
        }

        _articleRepository.Update(article);
        await _statusRepository.SaveChangesAsync();
    }

    private static void RevertLatestDeliveryBatch(Article article, ArticleDepartmentStatus deliveryStatus)
    {
        var latestBatch = article.DeliveryBatches
            .OrderByDescending(b => b.BatchNumber)
            .ThenByDescending(b => b.DeliveredAt)
            .FirstOrDefault();

        if (latestBatch == null)
            return;

        foreach (var item in latestBatch.Items)
        {
            var sb = article.SizeBreakdowns.FirstOrDefault(s =>
                string.Equals(s.SizeLabel.Trim(), item.SizeLabel.Trim(), StringComparison.OrdinalIgnoreCase));
            if (sb != null)
            {
                sb.DeliveredQuantity = Math.Max(0, sb.DeliveredQuantity - item.QuantityDelivered);
            }
        }

        article.DeliveredBGradeQuantity = Math.Max(0, article.DeliveredBGradeQuantity - latestBatch.BGradeDelivered);
        if (latestBatch.NoOfCartons.HasValue && article.NoOfCartons.HasValue)
        {
            article.NoOfCartons = Math.Max(0, article.NoOfCartons.Value - latestBatch.NoOfCartons.Value);
        }

        article.DeliveryBatches.Remove(latestBatch);

        var remainingDelivered = article.SizeBreakdowns.Sum(s => s.DeliveredQuantity) + article.DeliveredBGradeQuantity;
        deliveryStatus.OutputQuantity = remainingDelivered > 0 ? remainingDelivered : null;
        deliveryStatus.LossQuantity = remainingDelivered > 0
            ? Math.Max(0, (deliveryStatus.InputQuantity ?? 0) - remainingDelivered)
            : null;
    }
}
