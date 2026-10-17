using System;
using System.Collections.Generic;

namespace Application.DTOs.User;

public class UserResponseDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Category { get; set; } = "Both";
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int? DepartmentOrderIndex { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool HasSeparateTeams { get; set; }
    public List<string> TeamNames { get; set; } = new();
}
