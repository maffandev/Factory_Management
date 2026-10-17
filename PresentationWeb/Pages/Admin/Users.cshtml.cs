using Application.DTOs.User;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PresentationWeb.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel : PageModel
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;
    private readonly IDepartmentRepository _departmentRepository;

    public UsersModel(
        IUserService userService,
        IAuthService authService,
        IDepartmentRepository departmentRepository)
    {
        _userService = userService;
        _authService = authService;
        _departmentRepository = departmentRepository;
    }

    public List<UserResponseDto> Users { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public int StitchingDepartmentId { get; set; }

    public List<StitchingUserTeamGroup> TeamGroups { get; set; } = new();

    public class StitchingUserTeamGroup
    {
        public int? UserId { get; set; }
        public string OwnerLabel { get; set; } = string.Empty;
        public StitchingTeam TeamA { get; set; } = null!;
        public StitchingTeam TeamB { get; set; } = null!;
    }

    public async Task OnGetAsync()
    {
        Users = await _userService.GetAllUsersAsync();
        Departments = await _departmentRepository.GetAllAsync();
        StitchingDepartmentId = Departments.FirstOrDefault(d => d.Type == DepartmentType.Stitching)?.Id ?? 0;

        await LoadStitchingTeamsAsync();
    }

    // Create new user (Manager or Reader with Knit / Woven / Both category + optional Stitching separate teams)
    public async Task<IActionResult> OnPostCreateUserAsync(
        string username,
        string password,
        string role,
        int? departmentId,
        string category,
        bool createSeparateTeams = true,
        string? teamAName = null,
        string? teamBName = null)
    {
        try
        {
            await _userService.CreateUserAsync(new UserCreateDto
            {
                Username = username,
                Password = password,
                Role = role,
                DepartmentId = departmentId,
                Category = category,
                CreateSeparateTeams = createSeparateTeams,
                TeamAName = teamAName,
                TeamBName = teamBName
            });

            TempData["SuccessMessage"] = $"User '{username}' ({role} — {category}) created successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Admin/Users");
    }

    // Change username + department + category + active status + optional Stitching separate teams
    public async Task<IActionResult> OnPostUpdateUserAsync(
        int id,
        string newUsername,
        int? newDepartmentId,
        string? newCategory,
        bool? isActive,
        bool? createSeparateTeams = null,
        string? teamAName = null,
        string? teamBName = null)
    {
        try
        {
            await _userService.AdminUpdateUserAsync(
                id,
                newUsername,
                newDepartmentId,
                newCategory,
                isActive,
                createSeparateTeams,
                teamAName,
                teamBName);

            TempData["SuccessMessage"] = "User updated successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Admin/Users");
    }

    // Reset password
    public async Task<IActionResult> OnPostResetPasswordAsync(
        int id,
        string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) ||
            newPassword.Length < 4)
        {
            TempData["ErrorMessage"] =
                "Password must be at least 4 characters.";

            return RedirectToPage("/Admin/Users");
        }

        try
        {
            await _authService.AdminResetUserPasswordAsync(
                id,
                newPassword);

            TempData["SuccessMessage"] =
                "Password reset successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Admin/Users");
    }

    // Update Team A and Team B names for a Stitching user
    public async Task<IActionResult> OnPostUpdateTeamsAsync(
        int teamAId,
        string teamAName,
        int teamBId,
        string teamBName)
    {
        if (string.IsNullOrWhiteSpace(teamAName) ||
            string.IsNullOrWhiteSpace(teamBName))
        {
            TempData["ErrorMessage"] =
                "Both Team A and Team B names are required.";

            return RedirectToPage("/Admin/Users");
        }

        teamAName = teamAName.Trim();
        teamBName = teamBName.Trim();

        if (teamAName.Length > 50 || teamBName.Length > 50)
        {
            TempData["ErrorMessage"] =
                "Team names cannot be longer than 50 characters.";

            return RedirectToPage("/Admin/Users");
        }

        if (string.Equals(
                teamAName,
                teamBName,
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] =
                "Team A and Team B must have different names.";

            return RedirectToPage("/Admin/Users");
        }

        try
        {
            var stitchingDepartment =
                await _departmentRepository.GetByTypeAsync(
                    DepartmentType.Stitching);

            if (stitchingDepartment == null)
            {
                TempData["ErrorMessage"] =
                    "Stitching department was not found.";

                return RedirectToPage("/Admin/Users");
            }

            var teamA = stitchingDepartment.Teams
                .FirstOrDefault(t => t.Id == teamAId);

            var teamB = stitchingDepartment.Teams
                .FirstOrDefault(t => t.Id == teamBId);

            if (teamA == null || teamB == null)
            {
                TempData["ErrorMessage"] =
                    "Selected stitching teams were not found.";

                return RedirectToPage("/Admin/Users");
            }

            teamA.Name = teamAName;
            teamB.Name = teamBName;

            _departmentRepository.Update(stitchingDepartment);
            await _departmentRepository.SaveChangesAsync();

            TempData["SuccessMessage"] =
                "Stitching team names updated successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Admin/Users");
    }

    private async Task LoadStitchingTeamsAsync()
    {
        var stitchingDepartment =
            await _departmentRepository.GetByTypeAsync(
                DepartmentType.Stitching);

        var activeTeams = stitchingDepartment?.Teams
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .ToList()
            ?? new List<StitchingTeam>();

        var grouped = activeTeams.GroupBy(t => t.UserId);
        foreach (var g in grouped)
        {
            var list = g.ToList();
            if (list.Count >= 2)
            {
                var ownerUser = g.Key.HasValue
                    ? Users.FirstOrDefault(u => u.Id == g.Key.Value)
                    : null;

                var label = ownerUser != null
                    ? $"{ownerUser.Username} ({ownerUser.Category})"
                    : "Default Stitching Teams";

                TeamGroups.Add(new StitchingUserTeamGroup
                {
                    UserId = g.Key,
                    OwnerLabel = label,
                    TeamA = list[0],
                    TeamB = list[1]
                });
            }
        }
    }
}
