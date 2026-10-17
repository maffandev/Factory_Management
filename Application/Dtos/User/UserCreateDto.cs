namespace Application.DTOs.User;

public class UserCreateDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Manager";
    public int? DepartmentId { get; set; }
    public string Category { get; set; } = "Both";
    public bool CreateSeparateTeams { get; set; } = true;
    public string? TeamAName { get; set; }
    public string? TeamBName { get; set; }
}
