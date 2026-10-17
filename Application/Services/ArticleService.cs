using Application.DTOs.Article;
using Application.DTOs.Fabric;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class ArticleService : IArticleService
{
    private readonly IArticleRepository _articleRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IFabricService _fabricService;

    public ArticleService(
        IArticleRepository articleRepository,
        IDepartmentRepository departmentRepository,
        IFabricService fabricService)
    {
        _articleRepository = articleRepository;
        _departmentRepository = departmentRepository;
        _fabricService = fabricService;
    }

    public async Task<List<ArticleResponseDto>> GetAllAsync()
    {
        var articles = await _articleRepository.GetAllActiveAsync();

        return articles
            .Select(MapToDto)
            .ToList();
    }

    public async Task<ArticleResponseDto?> GetByIdAsync(int id)
    {
        var article = await _articleRepository.GetByIdAsync(id);

        return article == null
            ? null
            : MapToDto(article);
    }

    public async Task<ArticleResponseDto> CreateAsync(
        ArticleCreateDto dto,
        int createdByUserId)
    {
        return await _articleRepository.ExecuteInTransactionAsync(
            async () =>
            {
                dto.ArticleCode = (dto.ArticleCode ?? string.Empty).Trim();
                dto.CompanyName = (dto.CompanyName ?? string.Empty).Trim();

                var codeExists =
                    await _articleRepository.ArticleCodeExistsAsync(
                        dto.ArticleCode);

                if (codeExists)
                {
                    throw new InvalidOperationException(
                        "This article code already exists.");
                }

                var category = Enum.TryParse<ArticleCategory>(dto.Category, true, out var parsedCat)
                    ? parsedCat
                    : ArticleCategory.Knit;

                var article = new Article
                {
                    CompanyName = dto.CompanyName,
                    ArticleCode = dto.ArticleCode,
                    Color = dto.Color,
                    Category = category,
                    OrderDate = dto.OrderDate == default
                        ? PakistanTime.Today
                        : dto.OrderDate,
                    DeliveryDate = dto.DeliveryDate,
                    Season = dto.Season,
                    EmbellishmentEmbroidery =
                        dto.EmbellishmentEmbroidery,
                    EmbellishmentPrinting =
                        dto.EmbellishmentPrinting,
                    EmbellishmentHandwork =
                        dto.EmbellishmentHandwork,
                    Quantity = dto.Quantity,
                    PricePerPiece = dto.PricePerPiece,
                    PriceTotal =
                        dto.PricePerPiece.HasValue &&
                        dto.Quantity.HasValue
                            ? dto.PricePerPiece.Value *
                              dto.Quantity.Value
                            : null,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = PakistanTime.Now,
                    UpdatedAt = PakistanTime.Now,
                    IsActive = true
                };

                // Create and link new fabrics.
                foreach (var newFabricLink in dto.NewFabricLinks)
                {
                    var createdFabric =
                        await _fabricService.CreateAsync(
                            newFabricLink.Fabric);

                    article.FabricLinks.Add(
                        new ArticleFabric
                        {
                            FabricId = createdFabric.Id,
                            QuantityUsed =
                                newFabricLink.QuantityUsed
                        });

                    await _fabricService.DeductForArticleAsync(
                        createdFabric.Id,
                        newFabricLink.QuantityUsed);
                }

                // Link existing fabrics.
                foreach (var fabric in dto.Fabrics)
                {
                    if (article.FabricLinks.Any(
                        x => x.FabricId == fabric.FabricId))
                    {
                        throw new InvalidOperationException(
                            "Fabric is already linked to this article.");
                    }

                    article.FabricLinks.Add(
                        new ArticleFabric
                        {
                            FabricId = fabric.FabricId,
                            QuantityUsed = fabric.QuantityUsed
                        });

                    await _fabricService.DeductForArticleAsync(
                        fabric.FabricId,
                        fabric.QuantityUsed);
                }

                var allDepartments =
                    await _departmentRepository.GetAllAsync();

                BuildWorkflow(
                    article,
                    dto,
                    allDepartments);

                await _articleRepository.AddAsync(article);
                await _articleRepository.SaveChangesAsync();

                return MapToDto(article);
            });
    }

    private static void BuildWorkflow(
        Article article,
        ArticleCreateDto dto,
        List<Department> allDepartments)
    {
        var orderedDepts = new List<Department>();

        if (dto.DepartmentOrder is { Count: > 0 })
        {
            foreach (var departmentId in dto.DepartmentOrder)
            {
                var department =
                    allDepartments.FirstOrDefault(
                        d => d.Id == departmentId);

                if (department == null || orderedDepts.Any(d => d.Id == department.Id))
                    continue;

                if (department.Type == DepartmentType.Printing && !dto.EmbellishmentPrinting)
                    continue;
                if (department.Type == DepartmentType.Embroidery && !dto.EmbellishmentEmbroidery)
                    continue;
                if (department.Type == DepartmentType.HandWork && !dto.EmbellishmentHandwork)
                    continue;

                orderedDepts.Add(department);
            }
        }
        else
        {
            foreach (var department in allDepartments.OrderBy(d => d.OrderIndex))
            {
                var applies = department.Type switch
                {
                    DepartmentType.Printing => dto.EmbellishmentPrinting,
                    DepartmentType.Embroidery => dto.EmbellishmentEmbroidery,
                    DepartmentType.HandWork => dto.EmbellishmentHandwork,
                    _ => true
                };

                if (applies)
                    orderedDepts.Add(department);
            }
        }

        // Ensure Quality & Packing and Delivery always stay at the very end of the workflow
        // so Printing/Embroidery/Handwork/Stitching never end up waiting on Delivery.
        var prodDepts = orderedDepts
            .Where(d => d.Type != DepartmentType.QualityAndPacking && d.Type != DepartmentType.Delivery)
            .ToList();
        var finishDepts = orderedDepts
            .Where(d => d.Type == DepartmentType.QualityAndPacking || d.Type == DepartmentType.Delivery)
            .OrderBy(d => d.OrderIndex)
            .ToList();

        var sequence = 1;
        foreach (var dept in prodDepts.Concat(finishDepts))
        {
            article.DepartmentStatuses.Add(
                new ArticleDepartmentStatus
                {
                    DepartmentId = dept.Id,
                    SequenceNumber = sequence++,
                    Status = DepartmentStatus.Pending
                });
        }
    }

    public async Task UpdateArticleAsync(
        int articleId,
        ArticleUpdateDto dto)
    {
        await _articleRepository.ExecuteInTransactionAsync(
            async () =>
            {
                var article =
                    await _articleRepository.GetByIdAsync(articleId);

                if (article == null)
                {
                    throw new InvalidOperationException(
                        "Article not found.");
                }

                var cuttingStarted =
                    article.DepartmentStatuses.Any(
                        departmentStatus =>
                            departmentStatus.Department != null &&
                            departmentStatus.Department.Type ==
                                DepartmentType.Cutting &&
                            departmentStatus.Status !=
                                DepartmentStatus.Pending);

                article.CompanyName = dto.CompanyName;
                article.Color = dto.Color;
                if (Enum.TryParse<ArticleCategory>(dto.Category, true, out var parsedCat))
                {
                    article.Category = parsedCat;
                }

                article.DeliveryDate = dto.DeliveryDate;
                article.Season = dto.Season;
                article.Quantity = dto.Quantity;
                article.PricePerPiece = dto.PricePerPiece;

                article.PriceTotal =
                    dto.PricePerPiece.HasValue &&
                    dto.Quantity.HasValue
                        ? dto.PricePerPiece.Value *
                          dto.Quantity.Value
                        : null;

                article.IsPinned = dto.IsPinned;
                article.StitchedBy = dto.StitchedBy;
                article.UpdatedAt = PakistanTime.Now;

                // Replace alternate codes.
                var existingCodes =
                    article.AlternateCodes
                        .Select(code => code.Code)
                        .ToList();

                foreach (var oldCode in existingCodes)
                {
                    if (!dto.AlternateCodes.Contains(oldCode))
                    {
                        var codeToRemove =
                            article.AlternateCodes.FirstOrDefault(
                                code => code.Code == oldCode);

                        if (codeToRemove != null)
                        {
                            article.AlternateCodes.Remove(
                                codeToRemove);
                        }
                    }
                }

                foreach (var code in dto.AlternateCodes)
                {
                    if (!article.AlternateCodes.Any(
                        existing => existing.Code == code))
                    {
                        article.AlternateCodes.Add(
                            new ArticleAlternateCode
                            {
                                Code = code
                            });
                    }
                }

                await SyncWorkflowForUpdateAsync(
                    article,
                    dto);

                var desiredFabricLinks =
                    dto.Fabrics.ToList();

                foreach (var newFabricLink in dto.NewFabricLinks)
                {
                    if (cuttingStarted)
                    {
                        throw new InvalidOperationException(
                            "Cannot add a new fabric because Cutting has already started.");
                    }

                    var createdFabric =
                        await _fabricService.CreateAsync(
                            newFabricLink.Fabric);

                    desiredFabricLinks.Add(
                        new ArticleFabricDto
                        {
                            FabricId = createdFabric.Id,
                            QuantityUsed =
                                newFabricLink.QuantityUsed
                        });
                }

                var currentLinks =
                    article.FabricLinks.ToList();

                foreach (var currentLink in currentLinks)
                {
                    var desiredLink =
                        desiredFabricLinks.FirstOrDefault(
                            link => link.FabricId ==
                                    currentLink.FabricId);

                    if (desiredLink == null)
                    {
                        if (cuttingStarted)
                        {
                            throw new InvalidOperationException(
                                "Cannot remove fabric because Cutting has already started.");
                        }

                        await _fabricService.ReturnForArticleAsync(
                            currentLink.FabricId,
                            currentLink.QuantityUsed);

                        article.FabricLinks.Remove(
                            currentLink);
                    }
                    else
                    {
                        var quantityDifference =
                            desiredLink.QuantityUsed -
                            currentLink.QuantityUsed;

                        if (quantityDifference != 0)
                        {
                            if (quantityDifference > 0)
                            {
                                await _fabricService
                                    .DeductForArticleAsync(
                                        currentLink.FabricId,
                                        quantityDifference);
                            }
                            else
                            {
                                await _fabricService
                                    .ReturnForArticleAsync(
                                        currentLink.FabricId,
                                        -quantityDifference);
                            }

                            currentLink.QuantityUsed =
                                desiredLink.QuantityUsed;
                        }
                    }
                }

                foreach (var desiredLink in desiredFabricLinks)
                {
                    if (!article.FabricLinks.Any(
                        link => link.FabricId ==
                                desiredLink.FabricId))
                    {
                        if (cuttingStarted)
                        {
                            throw new InvalidOperationException(
                                "Cannot add fabric because Cutting has already started.");
                        }

                        article.FabricLinks.Add(
                            new ArticleFabric
                            {
                                FabricId =
                                    desiredLink.FabricId,
                                QuantityUsed =
                                    desiredLink.QuantityUsed
                            });

                        await _fabricService
                            .DeductForArticleAsync(
                                desiredLink.FabricId,
                                desiredLink.QuantityUsed);
                    }
                }

                _articleRepository.Update(article);

                await _articleRepository.SaveChangesAsync();
            });
    }

    private async Task SyncWorkflowForUpdateAsync(
        Article article,
        ArticleUpdateDto dto)
    {
        var allDepartments =
            await _departmentRepository.GetAllAsync();

        var embellishmentTypes =
            new[]
            {
                (
                    Type: DepartmentType.Printing,
                    Checked: dto.EmbellishmentPrinting
                ),
                (
                    Type: DepartmentType.Embroidery,
                    Checked: dto.EmbellishmentEmbroidery
                ),
                (
                    Type: DepartmentType.HandWork,
                    Checked: dto.EmbellishmentHandwork
                )
            };

        foreach (var item in embellishmentTypes)
        {
            var existingStatus =
                article.DepartmentStatuses.FirstOrDefault(
                    status =>
                        status.Department != null &&
                        status.Department.Type == item.Type);

            if (item.Checked)
            {
                if (existingStatus == null)
                {
                    var department =
                        allDepartments.FirstOrDefault(
                            d => d.Type == item.Type);

                    if (department == null)
                        continue;

                    article.DepartmentStatuses.Add(
                        new ArticleDepartmentStatus
                        {
                            DepartmentId = department.Id,
                            Department = department,
                            SequenceNumber = 999,
                            Status = DepartmentStatus.Pending
                        });
                }
            }
            else
            {
                if (existingStatus != null &&
                    existingStatus.Status ==
                        DepartmentStatus.Pending)
                {
                    article.DepartmentStatuses.Remove(
                        existingStatus);
                }
                else if (existingStatus != null)
                {
                    throw new InvalidOperationException(
                        $"Cannot remove '{existingStatus.Department?.Name ?? "department"}' from workflow because its work has already started or completed.");
                }
            }
        }

        // Re-sequence all statuses:
        // 1) If dto.DepartmentOrder is provided, order according to dto.DepartmentOrder (with any missing ones in natural OrderIndex order).
        // 2) Otherwise, order newly added (SequenceNumber == 999) into their natural OrderIndex position before Stitching / QualityAndPacking / Delivery.
        // 3) Always keep Quality & Packing and Delivery at the very end.
        var statusesList = article.DepartmentStatuses.ToList();
        foreach (var st in statusesList)
        {
            st.Department ??= allDepartments.FirstOrDefault(d => d.Id == st.DepartmentId)!;
        }

        List<ArticleDepartmentStatus> orderedStatuses;
        if (dto.DepartmentOrder is { Count: > 0 })
        {
            orderedStatuses = statusesList
                .OrderBy(s =>
                {
                    var idx = dto.DepartmentOrder.IndexOf(s.DepartmentId);
                    return idx >= 0 ? idx : 100 + (s.Department?.OrderIndex ?? 50);
                })
                .ToList();
        }
        else
        {
            orderedStatuses = statusesList
                .OrderBy(s => s.SequenceNumber == 999
                    ? (s.Department?.OrderIndex ?? 5) * 10
                    : s.SequenceNumber * 10)
                .ToList();
        }

        var prodStatuses = orderedStatuses
            .Where(s => s.Department?.Type != DepartmentType.QualityAndPacking &&
                        s.Department?.Type != DepartmentType.Delivery)
            .ToList();
        var finishStatuses = orderedStatuses
            .Where(s => s.Department?.Type == DepartmentType.QualityAndPacking ||
                        s.Department?.Type == DepartmentType.Delivery)
            .OrderBy(s => s.Department?.OrderIndex ?? 99)
            .ToList();

        var seq = 1;
        foreach (var st in prodStatuses.Concat(finishStatuses))
        {
            st.SequenceNumber = seq++;
        }

        article.EmbellishmentPrinting = dto.EmbellishmentPrinting;
        article.EmbellishmentEmbroidery = dto.EmbellishmentEmbroidery;
        article.EmbellishmentHandwork = dto.EmbellishmentHandwork;
    }

    public async Task TogglePinAsync(int articleId)
    {
        var article =
            await _articleRepository.GetByIdAsync(articleId);

        if (article == null)
        {
            throw new InvalidOperationException(
                "Article not found.");
        }

        if (article.IsPinned)
        {
            article.IsPinned = false;
        }
        else
        {
            var pinned =
                await _articleRepository.GetPinnedAsync();

            if (pinned.Count >= 5)
            {
                throw new InvalidOperationException(
                    "Maximum 5 pinned articles.");
            }

            article.IsPinned = true;
        }

        article.UpdatedAt = PakistanTime.Now;

        _articleRepository.Update(article);

        await _articleRepository.SaveChangesAsync();
    }

    private static ArticleResponseDto MapToDto(Article article)
    {
        var aGrade =
            article.SizeBreakdowns.Sum(
                size => size.Quantity);

        var bGrade =
            article.BGradeQuantity ?? 0;

        var deliveredAGrade =
            article.SizeBreakdowns.Sum(
                size => size.DeliveredQuantity);

        var deliveredBGrade =
            article.DeliveredBGradeQuantity;

        var totalDeliverable = aGrade + bGrade;
        var totalDelivered = deliveredAGrade + deliveredBGrade;
        var remainingDeliverable = Math.Max(0, totalDeliverable - totalDelivered);

        var totalDepartmentLoss =
            article.DepartmentStatuses.Sum(
                status => status.LossQuantity ?? 0);

        var orderedStatuses = article.DepartmentStatuses
            .OrderBy(s => s.SequenceNumber)
            .ToList();

        var currentStep = !article.IsDelivered
            ? orderedStatuses.FirstOrDefault(s => s.Status != DepartmentStatus.Done)
            : null;

        string? currentDeptName = null;
        string? currentDeptStatus = null;
        var currentDeptDays = 0;
        var isDeptDelayed = false;

        if (currentStep != null)
        {
            currentDeptName = currentStep.Department?.Name ?? "Department";
            currentDeptStatus = currentStep.Status.ToString();

            var prevDone = orderedStatuses
                .Where(s => s.SequenceNumber < currentStep.SequenceNumber && s.Status == DepartmentStatus.Done)
                .OrderByDescending(s => s.SequenceNumber)
                .FirstOrDefault();

            var holdSince = prevDone?.EndedAt ?? currentStep.StartedAt ?? article.CreatedAt;
            if (currentStep.StartedAt.HasValue && currentStep.StartedAt.Value < holdSince)
                holdSince = currentStep.StartedAt.Value;

            var elapsed = (PakistanTime.Now - holdSince).TotalDays;
            currentDeptDays = Math.Max(0, (int)Math.Floor(elapsed));
            isDeptDelayed = elapsed >= PakistanTime.DepartmentDelayThresholdDays;
        }

        return new ArticleResponseDto
        {
            Id = article.Id,
            CompanyName = article.CompanyName,
            ArticleCode = article.ArticleCode,
            Color = article.Color,
            Category = article.Category.ToString(),
            OrderDate = article.OrderDate,
            DeliveryDate = article.DeliveryDate,
            Season = article.Season,
            EmbellishmentEmbroidery =
                article.EmbellishmentEmbroidery,
            EmbellishmentPrinting =
                article.EmbellishmentPrinting,
            EmbellishmentHandwork =
                article.EmbellishmentHandwork,
            Quantity = article.Quantity,
            PricePerPiece = article.PricePerPiece,
            PriceTotal = article.PriceTotal,
            IsPinned = article.IsPinned,
            DoneDepartments =
                article.DepartmentStatuses.Count(
                    status =>
                        status.Status ==
                        DepartmentStatus.Done),
            TotalDepartments =
                article.DepartmentStatuses.Count,
            CurrentDepartmentName = currentDeptName,
            CurrentDepartmentStatus = currentDeptStatus,
            CurrentDepartmentDays = currentDeptDays,
            IsDepartmentDelayed = isDeptDelayed,
            IsDelivered = article.IsDelivered,
            DeliveredAt = article.DeliveredAt,
            CuttingStarted =
                article.DepartmentStatuses.Any(
                    status =>
                        status.Department != null &&
                        status.Department.Type ==
                            DepartmentType.Cutting &&
                        status.Status !=
                            DepartmentStatus.Pending),
            AssignedTeamName =
                article.AssignedTeam?.Name,
            StitchedBy = article.StitchedBy,
            BGradeQuantity = article.BGradeQuantity,
            AGradeQuantity = aGrade,
            DeliveredAGradeQuantity = deliveredAGrade,
            DeliveredBGradeQuantity = deliveredBGrade,
            TotalDeliverableQuantity = totalDeliverable,
            TotalDeliveredQuantity = totalDelivered,
            RemainingDeliverableQuantity = remainingDeliverable,
            TotalLossQuantity = totalDepartmentLoss,
            LossQuantity =
                article.Quantity.HasValue
                    ? article.Quantity.Value -
                      aGrade -
                      bGrade
                    : null,
            SizeBreakdowns =
                article.SizeBreakdowns
                    .OrderBy(size => size.OrderIndex)
                    .Select(size =>
                        new SizeBreakdownEntryDto
                        {
                            SizeLabel = size.SizeLabel,
                            OrderIndex = size.OrderIndex,
                            Quantity = size.Quantity,
                            DeliveredQuantity = size.DeliveredQuantity
                        })
                    .ToList(),
            CuttingSizeBreakdowns = article.CuttingSizeBreakdowns
                .OrderBy(size => size.OrderIndex)
                .Select(size => new SizeBreakdownEntryDto
                {
                    SizeLabel = size.SizeLabel,
                    OrderIndex = size.OrderIndex,
                    Quantity = size.Quantity
                }).ToList(),
            DeliveryBatches = (article.DeliveryBatches ?? new List<ArticleDeliveryBatch>())
                .OrderByDescending(b => b.BatchNumber)
                .ThenByDescending(b => b.DeliveredAt)
                .Select(b => new DeliveryBatchDto
                {
                    Id = b.Id,
                    BatchNumber = b.BatchNumber,
                    BGradeDelivered = b.BGradeDelivered,
                    TotalDeliveredInBatch = b.TotalDeliveredInBatch,
                    PackedBy = b.PackedBy,
                    CheckedBy = b.CheckedBy,
                    NoOfCartons = b.NoOfCartons,
                    Note = b.Note,
                    DeliveredByUsername = b.DeliveredBy?.Username ?? string.Empty,
                    DeliveredAt = b.DeliveredAt,
                    Items = (b.Items ?? new List<ArticleDeliveryBatchItem>())
                        .OrderBy(i => i.OrderIndex)
                        .Select(i => new DeliveryBatchItemDto
                        {
                            SizeLabel = i.SizeLabel,
                            OrderIndex = i.OrderIndex,
                            QuantityDelivered = i.QuantityDelivered
                        })
                        .ToList()
                })
                .ToList(),
            CuttingSizeTotal = article.CuttingSizeBreakdowns.Sum(x => x.Quantity),
            PreQualityLossQuantity = Math.Max(0,
                article.CuttingSizeBreakdowns.Sum(x => x.Quantity) -
                (article.DepartmentStatuses.FirstOrDefault(x => x.Department != null && x.Department.Type == DepartmentType.QualityAndPacking)?.InputQuantity ?? article.CuttingSizeBreakdowns.Sum(x => x.Quantity))),
            CGradeQuantity = Math.Max(0,
                (article.DepartmentStatuses.FirstOrDefault(x => x.Department != null && x.Department.Type == DepartmentType.QualityAndPacking)?.InputQuantity ?? 0) - aGrade - bGrade),
            PackedBy = article.PackedBy,
            CheckedBy = article.CheckedBy,
            NoOfCartons = article.NoOfCartons,
            CreatedByUsername =
                article.CreatedBy?.Username ??
                string.Empty,
            CreatedAt = article.CreatedAt,
            AlternateCodes =
                article.AlternateCodes
                    .Select(code => code.Code)
                    .ToList(),
            Fabrics =
                article.FabricLinks
                    .Select(link =>
                        new ArticleFabricDto
                        {
                            FabricId = link.FabricId,
                            FabricCode =
                                link.Fabric?.FabricCode,
                            QuantityUsed =
                                link.QuantityUsed,
                            FabricType =
                                link.Fabric?.FabricType,
                            InvNum =
                                link.Fabric?.InvNum,
                            Color = link.Fabric?.Color,
                            Unit = link.Fabric?.Unit.ToString(),
                            Status =
                                link.Fabric?.Status
                                    .ToString()
                        })
                    .ToList()
        };
    }
}
