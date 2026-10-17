using System;

namespace Domain.Entities;

public class StitchingTeam
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
