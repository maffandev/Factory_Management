using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace PresentationWeb.Pages.Reader;

[Authorize(Roles = "Reader")]
public class HistoryModel : PageModel
{
    private readonly IArticleService _articleService;

    public HistoryModel(IArticleService articleService)
    {
        _articleService = articleService;
    }

    public string UserCategory { get; set; } = "Both";

    private string GetCallerCategory() =>
        User.FindFirstValue("UserCategory") ?? "Both";

    public void OnGet()
    {
        UserCategory = GetCallerCategory();
    }

    public async Task<IActionResult> OnGetArticlesAsync(string? search, string? category, string? from, string? to)
    {
        var callerCat = GetCallerCategory();
        var articles = await _articleService.GetAllAsync();
        articles = articles.Where(a => a.IsDelivered).ToList();

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
                a.ArticleCode.ToLower().Contains(q) ||
                a.CompanyName.ToLower().Contains(q)).ToList();
        }

        if (DateTime.TryParse(from, out var fromDate))
            articles = articles.Where(a => a.DeliveredAt.HasValue && a.DeliveredAt.Value.Date >= fromDate.Date).ToList();
        if (DateTime.TryParse(to, out var toDate))
            articles = articles.Where(a => a.DeliveredAt.HasValue && a.DeliveredAt.Value.Date <= toDate.Date).ToList();

        var rows = articles.Select(a => new
        {
            a.Id,
            a.ArticleCode,
            a.CompanyName,
            a.Category,
            a.Color,
            Fabrics = a.Fabrics,
            a.Quantity,
            a.DeliveryDate,
            DeliveredAt = a.DeliveredAt
        }).OrderByDescending(r => r.DeliveredAt).ToList();

        return new JsonResult(rows);
    }
}
