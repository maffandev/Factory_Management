using Application.Interfaces;
using Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace PresentationWeb.Pages.Reader;

[Authorize(Roles = "Reader")]
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
    public string UserCategory { get; set; } = "Both";

    private string GetCallerCategory() =>
        User.FindFirstValue("UserCategory") ?? "Both";

    public async Task OnGetAsync()
    {
        UserCategory = GetCallerCategory();

        var articles = await _articleService.GetAllAsync();
        if (!string.Equals(UserCategory, "Both", StringComparison.OrdinalIgnoreCase))
        {
            articles = articles
                .Where(a => string.Equals(a.Category, UserCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ActiveArticles = articles.Count(a => !a.IsDelivered);

        if (User.HasClaim(c => c.Type == "ImpersonatedFrom"))
            ViewData["Users"] = await _userService.GetAllUsersAsync();
    }

    public async Task<IActionResult> OnGetArticlesLiveAsync(
        string? search,
        string? category,
        string? season,
        string? status,
        string? sort)
    {
        var callerCat = GetCallerCategory();
        var articles = await _articleService.GetAllAsync();
        var today = PakistanTime.Today;

        // Enforce strict category visibility if user is Knit or Woven
        if (!string.Equals(callerCat, "Both", StringComparison.OrdinalIgnoreCase))
        {
            articles = articles
                .Where(a => string.Equals(a.Category, callerCat, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
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
            return new
            {
                a.Id,
                a.CompanyName,
                a.ArticleCode,
                a.Category,
                a.Color,
                Fabrics = a.Fabrics,
                AlternateCodes = a.AlternateCodes,
                a.Quantity,
                a.DoneDepartments,
                a.TotalDepartments,
                a.CurrentDepartmentName,
                a.CurrentDepartmentStatus,
                a.CurrentDepartmentDays,
                a.IsDepartmentDelayed,
                a.TotalDeliverableQuantity,
                a.TotalDeliveredQuantity,
                a.RemainingDeliverableQuantity,
                a.DeliveryDate,
                DaysLeft = daysLeft,
                IsOverdue = daysLeft < 0,
                IsDueSoon = daysLeft >= 0 && daysLeft <= 7,
                a.IsDelivered
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(status))
        {
            rows = status switch
            {
                "delayed" => rows.Where(r => r.IsDepartmentDelayed).ToList(),
                "overdue" => rows.Where(r => r.IsOverdue).ToList(),
                "dueSoon" => rows.Where(r => r.IsDueSoon).ToList(),
                "delivered" => rows.Where(r => r.IsDelivered).ToList(),
                "active" => rows.Where(r => !r.IsDelivered).ToList(),
                _ => rows
            };
        }

        if (!string.IsNullOrWhiteSpace(sort))
        {
            rows = sort switch
            {
                "low" => rows.OrderBy(r => r.DaysLeft).ToList(),
                "high" => rows.OrderByDescending(r => r.DaysLeft).ToList(),
                _ => rows
            };
        }

        return new JsonResult(rows);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Login");
    }
}
