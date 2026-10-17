using Application.DTOs.Article;
using Application.DTOs.Workflow;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace PresentationWeb.Pages.Manager;

[Authorize(Roles = "Manager")]
public class DashboardModel : PageModel
{
    private readonly IWorkflowService _workflowService;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IQualityService _qualityService;
    private readonly IDeliveryService _deliveryService;
    private readonly IUserService _userService;
    private readonly IArticleService _articleService;

    public DashboardModel(
        IWorkflowService workflowService,
        IDepartmentRepository departmentRepository,
        IQualityService qualityService,
        IDeliveryService deliveryService,
        IUserService userService,
        IArticleService articleService)
    {
        _workflowService = workflowService;
        _departmentRepository = departmentRepository;
        _qualityService = qualityService;
        _deliveryService = deliveryService;
        _userService = userService;
        _articleService = articleService;
    }

    public Department? Department { get; set; }

    public List<ArticleDepartmentStatusDto> Statuses { get; set; } = new();
    public List<ArticleDepartmentStatusDto> SamplingApprovals { get; set; } = new();

    public List<StitchingTeam> Teams { get; set; } = new();

    public Dictionary<int, ArticleResponseDto> ArticleDetails { get; set; } = new();

    public Dictionary<int, List<ArticleDepartmentStatusDto>> LossBreakdown { get; set; } = new();

    public int DepartmentId { get; set; }

    public DepartmentType DepartmentType { get; set; }

    public string UserCategory { get; set; } = "Both";

    private string GetCallerCategory() =>
        User.FindFirstValue("UserCategory") ?? "Both";

    public async Task<IActionResult> OnGetAsync()
    {
        var deptClaim = User.FindFirstValue("DepartmentId");

        if (!int.TryParse(deptClaim, out var deptId))
            return RedirectToPage("/Login");

        DepartmentId = deptId;
        UserCategory = GetCallerCategory();

        Department = await _departmentRepository.GetByIdAsync(deptId);

        if (Department == null)
            return RedirectToPage("/Login");

        DepartmentType = Department.Type;

        Statuses =
            await _workflowService.GetPendingByDepartmentAsync(deptId, UserCategory);

        if (DepartmentType == DepartmentType.Pattern)
            SamplingApprovals = await _workflowService.GetSamplingAwaitingApprovalAsync(UserCategory);

        var currentUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var ownTeams = Department.Teams
            .Where(t => t.IsActive && t.UserId == currentUserId)
            .OrderBy(t => t.Id)
            .ToList();

        if (ownTeams.Count > 0)
        {
            Teams = ownTeams;
        }
        else
        {
            var firstOwnerId = Department.Teams
                .Where(t => t.IsActive && t.UserId != null)
                .OrderBy(t => t.Id)
                .Select(t => t.UserId)
                .FirstOrDefault();

            Teams = Department.Teams
                .Where(t => t.IsActive && (t.UserId == firstOwnerId || t.UserId == null))
                .OrderBy(t => t.Id)
                .Take(2)
                .ToList();
        }

        foreach (var status in Statuses)
        {
            if (ArticleDetails.ContainsKey(status.ArticleId))
                continue;

            var article =
                await _articleService.GetByIdAsync(status.ArticleId);

            if (article != null)
            {
                ArticleDetails[status.ArticleId] = article;
            }

            if (!LossBreakdown.ContainsKey(status.ArticleId))
            {
                var allStatuses = await _workflowService.GetByArticleAsync(status.ArticleId);
                LossBreakdown[status.ArticleId] = allStatuses;
            }
        }

        if (User.HasClaim(c => c.Type == "ImpersonatedFrom"))
        {
            ViewData["Users"] =
                await _userService.GetAllUsersAsync();
        }

        return Page();
    }

    private int GetCallerDeptId() =>
        int.TryParse(User.FindFirstValue("DepartmentId"), out var d) ? d : 0;

    private async Task<DepartmentType?> GetCallerDepartmentTypeAsync()
    {
        var deptId = GetCallerDeptId();
        if (deptId == 0) return null;
        var dept = await _departmentRepository.GetByIdAsync(deptId);
        return dept?.Type;
    }

    private async Task<bool> CallerOwnsDepartmentStatusAsync(int statusId)
    {
        var callerDeptId = GetCallerDeptId();
        if (callerDeptId == 0) return false;
        var status = await _workflowService.GetStatusByIdAsync(statusId);
        if (status == null || status.DepartmentId != callerDeptId)
            return false;

        var callerCat = GetCallerCategory();
        if (!string.Equals(callerCat, "Both", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(status.Category, callerCat, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<bool> CallerCanAccessArticleCategoryAsync(int articleId)
    {
        var callerCat = GetCallerCategory();
        if (string.Equals(callerCat, "Both", StringComparison.OrdinalIgnoreCase))
            return true;

        var article = await _articleService.GetByIdAsync(articleId);
        return article != null && string.Equals(article.Category, callerCat, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostStartAsync(
        int statusId,
        int? teamId)
    {
        if (!await CallerOwnsDepartmentStatusAsync(statusId))
        {
            TempData["ErrorMessage"] = "Access denied: this action does not belong to your department or category.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var userId = int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid)
            ? uid
            : 0;

        try
        {
            await _workflowService.StartWorkAsync(
                new StartDepartmentWorkDto
                {
                    ArticleDepartmentStatusId = statusId,
                    AssignedTeamId = teamId
                },
                userId);

            TempData["SuccessMessage"] =
                "Work started.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Manager/Dashboard");
    }

    public async Task<IActionResult> OnPostEndAsync(
        int statusId,
        int? outputQty,
        string? note,
        string? stitchedBy,
        string? cuttingSizeLabels,
        string? cuttingQuantities)
    {
        if (!await CallerOwnsDepartmentStatusAsync(statusId))
        {
            TempData["ErrorMessage"] = "Access denied: this action does not belong to your department or category.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var userId = int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid)
            ? uid
            : 0;

        try
        {
            var cuttingSizes = new List<CuttingSizeBreakdownEntryDto>();
            if (!string.IsNullOrWhiteSpace(cuttingSizeLabels))
            {
                var labels = cuttingSizeLabels.Split('|');
                var values = (cuttingQuantities ?? string.Empty).Split('|');
                if (labels.Length != values.Length)
                    throw new InvalidOperationException("The Cutting size table is incomplete.");
                for (var i = 0; i < labels.Length; i++)
                {
                    if (!int.TryParse(values[i], out var qty) || qty < 0)
                        throw new InvalidOperationException($"Invalid Cutting quantity for size '{labels[i]}'.");
                    cuttingSizes.Add(new CuttingSizeBreakdownEntryDto
                    { SizeLabel = labels[i], OrderIndex = i + 1, Quantity = qty });
                }
            }

            await _workflowService.EndWorkAsync(
                new EndDepartmentWorkDto
                {
                    ArticleDepartmentStatusId = statusId,
                    OutputQuantity = outputQty,
                    Note = note,
                    StitchedBy = stitchedBy,
                    CuttingSizeBreakdowns = cuttingSizes
                },
                userId);

            TempData["SuccessMessage"] =
                "Work completed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Manager/Dashboard");
    }

    public async Task<IActionResult> OnPostQualityAsync(
        int articleId,
        string? bGrade,
        string? sizeLabels,
        string? quantities,
        string? note)
    {
        var callerType = await GetCallerDepartmentTypeAsync();
        if (callerType != DepartmentType.QualityAndPacking || !await CallerCanAccessArticleCategoryAsync(articleId))
        {
            TempData["ErrorMessage"] = "Access denied: only the Quality & Packing manager for this category can submit grades.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var userId = int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid)
            ? uid
            : 0;

        var labels =
            (sizeLabels ?? string.Empty).Split('|');

        var qtyStrings =
            (quantities ?? string.Empty).Split('|');

        if (labels.Length != qtyStrings.Length ||
            labels.Length == 0 ||
            labels.Any(string.IsNullOrWhiteSpace))
        {
            TempData["ErrorMessage"] =
                "The size table is incomplete or invalid. Nothing was saved.";

            return RedirectToPage("/Manager/Dashboard");
        }

        var parsedBGrade = 0;

        if (!string.IsNullOrWhiteSpace(bGrade))
        {
            var cleanBGrade = bGrade.Trim();

            if (!cleanBGrade.All(char.IsDigit) ||
                !int.TryParse(
                    cleanBGrade,
                    out parsedBGrade))
            {
                TempData["ErrorMessage"] =
                    "B-Grade quantity must be a whole number. Nothing was saved.";

                return RedirectToPage("/Manager/Dashboard");
            }
        }

        var dto = new QualityGradeEntryDto
        {
            ArticleId = articleId,
            BGradeQuantity = parsedBGrade,
            SizeBreakdowns = new List<Application.DTOs.Workflow.SizeBreakdownEntryDto>()
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i].Trim();
            var rawQuantity = qtyStrings[i].Trim();

            if (string.IsNullOrWhiteSpace(rawQuantity))
            {
                dto.SizeBreakdowns.Add(
                    new Application.DTOs.Workflow.SizeBreakdownEntryDto
                    {
                        SizeLabel = label,
                        OrderIndex = i + 1,
                        Quantity = 0
                    });

                continue;
            }

            if (!rawQuantity.All(char.IsDigit) ||
                !int.TryParse(
                    rawQuantity,
                    out var parsedQuantity))
            {
                TempData["ErrorMessage"] =
                    $"Quantity for size '{label}' must be a whole number. Nothing was saved.";

                return RedirectToPage("/Manager/Dashboard");
            }

            dto.SizeBreakdowns.Add(
                new Application.DTOs.Workflow.SizeBreakdownEntryDto
                {
                    SizeLabel = label,
                    OrderIndex = i + 1,
                    Quantity = parsedQuantity
                });
        }

        dto.Note = note;

        try
        {
            await _qualityService.SubmitGradesAsync(
                dto,
                userId);

            TempData["SuccessMessage"] =
                "Quality grades submitted successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Manager/Dashboard");
    }

    // Delivery (partial size-wise + B-Grade or full delivery).
    public async Task<IActionResult> OnPostDeliveryAsync(
        int articleId,
        string? sizeLabels,
        string? sizeQuantities,
        string? bGradeToDeliver,
        string? packedBy,
        string? checkedBy,
        int? noOfCartons,
        string? note)
    {
        var callerTypeDelivery = await GetCallerDepartmentTypeAsync();
        if (callerTypeDelivery != DepartmentType.Delivery || !await CallerCanAccessArticleCategoryAsync(articleId))
        {
            TempData["ErrorMessage"] = "Access denied: only the Delivery manager for this category can confirm delivery.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var userId = int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid)
            ? uid
            : 0;

        var sizeDeliveries = new List<SizeDeliveryEntryDto>();
        if (!string.IsNullOrWhiteSpace(sizeLabels))
        {
            var labels = sizeLabels.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var qtys = (sizeQuantities ?? string.Empty).Split('|');
            if (labels.Length != qtys.Length)
            {
                TempData["ErrorMessage"] = "The delivery size table is incomplete or invalid.";
                return RedirectToPage("/Manager/Dashboard");
            }

            for (var i = 0; i < labels.Length; i++)
            {
                var raw = qtys[i].Trim();
                var parsed = 0;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    if (!int.TryParse(raw, out parsed) || parsed < 0)
                    {
                        TempData["ErrorMessage"] = $"Invalid delivery quantity for size '{labels[i]}'.";
                        return RedirectToPage("/Manager/Dashboard");
                    }
                }
                sizeDeliveries.Add(new SizeDeliveryEntryDto
                {
                    SizeLabel = labels[i].Trim(),
                    QuantityToDeliver = parsed
                });
            }
        }

        var parsedBGrade = 0;
        if (!string.IsNullOrWhiteSpace(bGradeToDeliver))
        {
            if (!int.TryParse(bGradeToDeliver.Trim(), out parsedBGrade) || parsedBGrade < 0)
            {
                TempData["ErrorMessage"] = "B-Grade delivery quantity must be a whole number >= 0.";
                return RedirectToPage("/Manager/Dashboard");
            }
        }

        try
        {
            var isFullyDelivered = await _deliveryService.ConfirmDeliveryAsync(
                new DeliveryConfirmDto
                {
                    ArticleId = articleId,
                    PackedBy = packedBy,
                    CheckedBy = checkedBy,
                    NoOfCartons = noOfCartons,
                    Note = note,
                    BGradeToDeliver = parsedBGrade,
                    SizeDeliveries = sizeDeliveries
                },
                userId);

            TempData["SuccessMessage"] = isFullyDelivered
                ? "All pieces delivered! Article moved to history."
                : "Partial delivery batch recorded. Remaining pieces stay in queue.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Manager/Dashboard");
    }

    public async Task<IActionResult> OnPostReviewSamplingAsync(int statusId, bool approved, string? note)
    {
        var callerTypePattern = await GetCallerDepartmentTypeAsync();
        if (callerTypePattern != DepartmentType.Pattern)
        {
            TempData["ErrorMessage"] = "Access denied: only the Pattern manager can review sampling.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var status = await _workflowService.GetStatusByIdAsync(statusId);
        if (status == null || !await CallerCanAccessArticleCategoryAsync(status.ArticleId))
        {
            TempData["ErrorMessage"] = "Access denied: this article does not belong to your category.";
            return RedirectToPage("/Manager/Dashboard");
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        try
        {
            await _workflowService.ReviewSamplingAsync(statusId, approved, note, userId);
            TempData["SuccessMessage"] = approved ? "Sampling approved; Cutting is now unlocked." : "Sampling rejected; resampling returned to the Sampling manager.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToPage("/Manager/Dashboard");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();

        return RedirectToPage("/Login");
    }
}
