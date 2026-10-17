using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public UserCategory Category { get; set; } = UserCategory.Both;

    public string? Email { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = PakistanTime.Now;
}
