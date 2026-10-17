using Application.DTOs.Fabric;
using Application.Interfaces;
using Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PresentationWeb.Pages.Admin;

[Authorize(Roles = "Admin")]
public class FabricsModel : PageModel
{
    private readonly IFabricService _fabricService;

    public FabricsModel(IFabricService fabricService)
    {
        _fabricService = fabricService;
    }

    public List<FabricResponseDto> AllFabrics { get; set; } = new();
    public int InStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public decimal TotalAvailableStockValue { get; set; }

    [BindProperty]
    public FabricCreateDto AddFabric { get; set; } = new();

    [BindProperty]
    public FabricTopUpDto TopUp { get; set; } = new();

    [BindProperty]
    public FabricUpdateDto EditFabric { get; set; } = new();

    [BindProperty]
    public FabricOutDto FabricOut { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadFabricsAsync();
    }

    private async Task LoadFabricsAsync()
    {
        // Show every fabric, including out-of-stock ones (AvailableQuantity = 0).
        // Hiding them would leave no way to top a fabric up once its stock ran out.
        AllFabrics = await _fabricService.GetAllAsync();
        InStockCount = AllFabrics.Count(f => f.Status == "InStock");
        OutOfStockCount = AllFabrics.Count(f => f.Status != "InStock");
        TotalAvailableStockValue = AllFabrics.Sum(f => f.AvailableQuantity * f.Rate);
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        KeepOnlyErrorsFor("AddFabric.");
        if (!ModelState.IsValid)
        {
            // Re-render with Page() so field-level validation errors stay visible.
            // RedirectToPage would clear ModelState and lose them.
            await LoadFabricsAsync();
            return Page();
        }

        try
        {
            var created = await _fabricService.CreateAsync(AddFabric);
            var companyNote = string.IsNullOrWhiteSpace(AddFabric.FabricBy)
                ? " Fabric By was left empty, so 'Factory Management' was saved."
                : string.Empty;
            TempData["SuccessMessage"] = $"Fabric '{created.FabricCode} — {created.Color}' was created successfully.{companyNote}";
            return RedirectToPage("/Admin/FabricForm");
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Admin/FabricForm");
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        KeepOnlyErrorsFor("EditFabric.");
        if (!ModelState.IsValid)
        {
            await LoadFabricsAsync();
            return Page();
        }

        try
        {
            var updated = await _fabricService.UpdateAsync(EditFabric);
            TempData["SuccessMessage"] =
                $"Fabric '{updated.FabricCode} — {updated.Color}' was updated. " +
                "Company, Type, Invoice #, Unit and Rate were applied to every colour of this fabric code; " +
                "Date and Quantity were applied to this colour only.";
            return RedirectToPage("/Admin/FabricForm");
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Admin/FabricForm");
        }
    }

    public async Task<IActionResult> OnPostTopUpAsync()
    {
        KeepOnlyErrorsFor("TopUp.");
        if (!ModelState.IsValid || TopUp.AddedQuantity <= 0)
        {
            TempData["ErrorMessage"] = "Additional quantity must be greater than zero.";
            return RedirectToPage("/Admin/FabricForm");
        }

        try
        {
            TopUp.TopUpDate = PakistanTime.Now;
            await _fabricService.TopUpAsync(TopUp);
            TempData["SuccessMessage"] = "Fabric topped up successfully.";
            return RedirectToPage("/Admin/FabricForm");
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Admin/FabricForm");
        }
    }

    public async Task<IActionResult> OnPostFabricOutAsync()
    {
        KeepOnlyErrorsFor("FabricOut.");
        if (!ModelState.IsValid || FabricOut.OutQuantity <= 0)
        {
            TempData["ErrorMessage"] = "Out quantity must be greater than zero.";
            return RedirectToPage("/Admin/FabricForm");
        }

        try
        {
            await _fabricService.FabricOutAsync(FabricOut);
            TempData["SuccessMessage"] = "Fabric out recorded successfully.";
            return RedirectToPage("/Admin/FabricForm");
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Admin/FabricForm");
        }
    }

    /// <summary>
    /// This page has 3 [BindProperty] properties (AddFabric / TopUp / EditFabric) and ModelState
    /// is shared across the entire page. Razor Pages also validates default instances of other DTOs
    /// — and their errors appear at the ROOT-level keys (like "FabricType", "AddedQuantity"), without a prefix.
    /// Because of this, on "Create Fabric" POST, the "TopUp" and "EditFabric" DTOs caused ModelState to become invalid,
    /// silently failing the add operation. This helper keeps only the errors relevant to the current handler.
    /// </summary>
    private void KeepOnlyErrorsFor(string keepPrefix)
    {
        foreach (var key in ModelState.Keys.ToList())
        {
            if (!key.StartsWith(keepPrefix, StringComparison.Ordinal))
                ModelState.Remove(key);
        }
    }
}
