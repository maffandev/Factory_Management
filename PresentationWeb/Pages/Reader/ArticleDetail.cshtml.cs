using Application.DTOs.Article;
using Application.DTOs.Workflow;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace PresentationWeb.Pages.Reader;

[Authorize(Roles = "Reader")]
public class ArticleDetailModel : PageModel
{
    private readonly IArticleService _articleService;
    private readonly IWorkflowService _workflowService;

    public ArticleDetailModel(IArticleService articleService, IWorkflowService workflowService)
    {
        _articleService = articleService;
        _workflowService = workflowService;
    }

    public ArticleResponseDto? Article { get; set; }
    public List<ArticleDepartmentStatusDto> DepartmentStatuses { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Article = await _articleService.GetByIdAsync(id);
        if (Article == null)
            return NotFound();

        var userCategory = User.FindFirstValue("UserCategory") ?? "Both";
        if (!string.Equals(userCategory, "Both", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Article.Category, userCategory, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Reader/Dashboard");
        }

        DepartmentStatuses = await _workflowService.GetByArticleAsync(id);
        return Page();
    }
}
