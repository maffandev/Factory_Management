using Application.DTOs.Article;
using Application.Interfaces;
using Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PresentationWeb.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DashboardModel : PageModel
{
    private readonly IArticleService _articleService;
    private readonly IUserService _userService;

    public DashboardModel(IArticleService articleService, IUserService userService)
    {
        _articleService = articleService;
        _userService = userService;
    }

    public int ActiveArticles { get; set; }
    public int KnitArticles { get; set; }
    public int WovenArticles { get; set; }
    public int ManagerCount { get; set; }
    public int ReaderCount { get; set; }
    public List<ArticleResponseDto> DelayedArticles { get; set; } = new();

    public async Task OnGetAsync()
    {
        var articles = (await _articleService.GetAllAsync()).Where(a => !a.IsDelivered).ToList();
        ActiveArticles = articles.Count;
        KnitArticles = articles.Count(a => string.Equals(a.Category, "Knit", StringComparison.OrdinalIgnoreCase));
        WovenArticles = articles.Count(a => string.Equals(a.Category, "Woven", StringComparison.OrdinalIgnoreCase));
        DelayedArticles = articles.Where(a => a.IsDepartmentDelayed).ToList();

        var users = await _userService.GetAllUsersAsync();
        ManagerCount = users.Count(u => u.Role == "Manager" && u.IsActive);
        ReaderCount = users.Count(u => u.Role == "Reader" && u.IsActive);

        ViewData["Users"] = users;
    }

    // Live table data endpoint — called by the client via fetch on search/filter/sort.
    public async Task<IActionResult> OnGetArticlesLiveAsync(
        string? search,
        string? category,
        string? season,
        string? status,
        string? sort)
    {
        var articles = await _articleService.GetAllAsync();
        var today = PakistanTime.Today;

        // Hide delivered articles from the active dashboard.
        articles = articles.Where(a => !a.IsDelivered).ToList();

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            articles = articles
                .Where(a => string.Equals(a.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLower();
            articles = articles.Where(a =>
                a.CompanyName.ToLower().Contains(q) ||
                a.ArticleCode.ToLower().Contains(q) ||
                (a.Color != null && a.Color.ToLower().Contains(q)) ||
                a.AlternateCodes.Any(c => c.ToLower().Contains(q))).ToList();
        }

        if (!string.IsNullOrWhiteSpace(season) && season != "All")
            articles = articles.Where(a => a.Season == season).ToList();

        var rows = articles.Select(a =>
        {
            var daysLeft = (a.DeliveryDate.Date - today).Days;
            return new ArticleRow
            {
                Id = a.Id,
                IsPinned = a.IsPinned,
                IsDelivered = a.IsDelivered,
                CompanyName = a.CompanyName,
                ArticleCode = a.ArticleCode,
                Color = a.Color,
                Category = a.Category,
                Fabrics = a.Fabrics,
                AlternateCodes = a.AlternateCodes,
                Quantity = a.Quantity,
                DoneDepartments = a.DoneDepartments,
                TotalDepartments = a.TotalDepartments,
                CurrentDepartmentName = a.CurrentDepartmentName,
                CurrentDepartmentStatus = a.CurrentDepartmentStatus,
                CurrentDepartmentDays = a.CurrentDepartmentDays,
                IsDepartmentDelayed = a.IsDepartmentDelayed,
                TotalDeliverableQuantity = a.TotalDeliverableQuantity,
                TotalDeliveredQuantity = a.TotalDeliveredQuantity,
                RemainingDeliverableQuantity = a.RemainingDeliverableQuantity,
                DeliveryDate = a.DeliveryDate,
                DaysLeft = daysLeft,
                IsOverdue = daysLeft < 0,
                IsDueSoon = daysLeft >= 0 && daysLeft <= 7
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(status))
        {
            rows = status switch
            {
                "delayed" => rows.Where(r => r.IsDepartmentDelayed).ToList(),
                "overdue" => rows.Where(r => r.IsOverdue).ToList(),
                "dueSoon" => rows.Where(r => r.IsDueSoon).ToList(),
                _ => rows
            };
        }

        rows = sort switch
        {
            "low" => rows.OrderByDescending(r => r.IsPinned).ThenBy(r => r.DaysLeft).ToList(),
            "high" => rows.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.DaysLeft).ToList(),
            _ => rows.OrderByDescending(r => r.IsPinned).ToList()
        };

        return new JsonResult(rows);
    }

    public async Task<IActionResult> OnPostTogglePinAsync(int id)
    {
        try
        {
            await _articleService.TogglePinAsync(id);
            return new JsonResult(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Login");
    }
}

public class ArticleRow
{
    public int Id { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDelivered { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ArticleCode { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string Category { get; set; } = "Knit";
    public List<ArticleFabricDto> Fabrics { get; set; } = new();
    public List<string> AlternateCodes { get; set; } = new();
    public int? Quantity { get; set; }
    public int DoneDepartments { get; set; }
    public int TotalDepartments { get; set; }
    public string? CurrentDepartmentName { get; set; }
    public string? CurrentDepartmentStatus { get; set; }
    public int CurrentDepartmentDays { get; set; }
    public bool IsDepartmentDelayed { get; set; }
    public int TotalDeliverableQuantity { get; set; }
    public int TotalDeliveredQuantity { get; set; }
    public int RemainingDeliverableQuantity { get; set; }
    public DateTime DeliveryDate { get; set; }
    public int DaysLeft { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsDueSoon { get; set; }
}
